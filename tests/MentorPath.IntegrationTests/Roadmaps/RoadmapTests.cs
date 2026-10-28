using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Roadmaps;

public sealed class RoadmapTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IRoadmapService Roadmaps => App.Get<IRoadmapService>();

    [Fact]
    public async Task Only_published_exercises_of_the_items_topic_can_be_assigned()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var topic = await Data.TopicAsync(mentor);
        var otherTopic = await Data.TopicAsync(mentor);
        var draft = (await App.Get<IExerciseAuthoringService>().CreateDraftAsync(mentor.UserId,
            new(topic, ExerciseType.ArchitecturalDecision, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision)), default)).ShouldSucceed();
        var foreignTopicExercise = await Data.PublishedExerciseAsync(mentor, otherTopic, ExerciseType.ArchitecturalDecision);

        var roadmap = (await Roadmaps.CreateAsync(mentor.UserId, new(mentee.MentorshipId, "R", "Cel"), default)).ShouldSucceed();
        var item = (await App.Get<IRoadmapService>().AddTopicAsync(mentor.UserId, new(roadmap, topic, null), default)).ShouldSucceed();
        (await App.Get<IRoadmapService>().AssignExerciseAsync(mentor.UserId, new(item, draft, true, null), default))
            .ShouldFail(ErrorKind.Validation, "roadmap.exercise_not_published");
        (await App.Get<IRoadmapService>().AssignExerciseAsync(mentor.UserId, new(item, foreignTopicExercise, true, null), default))
            .ShouldFail(ErrorKind.Validation, "roadmap.exercise_topic_mismatch");
    }

    [Fact]
    public async Task Publication_requires_a_required_exercise_and_one_roadmap_per_mentorship()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var roadmap = (await Roadmaps.CreateAsync(mentor.UserId, new(mentee.MentorshipId, "R", "Cel"), default)).ShouldSucceed();
        (await App.Get<IRoadmapService>().PublishAsync(mentor.UserId, roadmap, default)).ShouldFail(ErrorKind.Validation, "roadmap.publish_invalid");

        var topic = await Data.TopicAsync(mentor);
        var exercise = await Data.PublishedExerciseAsync(mentor, topic, ExerciseType.QuizSingleChoice);
        var item = (await App.Get<IRoadmapService>().AddTopicAsync(mentor.UserId, new(roadmap, topic, null), default)).ShouldSucceed();
        (await App.Get<IRoadmapService>().AssignExerciseAsync(mentor.UserId, new(item, exercise, false, null), default)).ShouldSucceed();
        (await App.Get<IRoadmapService>().PublishAsync(mentor.UserId, roadmap, default)).ShouldFail(ErrorKind.Validation, "roadmap.publish_invalid");

        (await App.Get<IRoadmapService>().CreateAsync(mentor.UserId, new(mentee.MentorshipId, "Druga", "Cel"), default))
            .ShouldFail(ErrorKind.Conflict, "roadmap.already_exists");
    }

    [Fact]
    public async Task Mentee_sees_only_published_roadmap_in_mentor_defined_order()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var first = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice, publish: false);
        (await App.Get<IRoadmapService>().GetForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();

        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision, publish: false);
        var third = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.PracticalTask, publish: true);
        (await App.Get<IRoadmapService>().ReorderTopicsAsync(mentor.UserId, first.RoadmapId,
            [third.RoadmapItemId, first.RoadmapItemId, second.RoadmapItemId], default)).ShouldSucceed();

        var view = (await App.Get<IRoadmapService>().GetForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        view.Items.Select(i => i.RoadmapItemId).ShouldBe([third.RoadmapItemId, first.RoadmapItemId, second.RoadmapItemId]);

        (await App.Get<IRoadmapService>().SetTopicActiveAsync(mentor.UserId, second.RoadmapItemId, false, default)).ShouldSucceed();
        var afterWithdraw = (await App.Get<IRoadmapService>().GetForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        afterWithdraw.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Items_with_submissions_cannot_be_removed_only_withdrawn()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);

        (await App.Get<IRoadmapService>().RemoveAssignmentAsync(mentor.UserId, asg.AssignmentId, default)).ShouldFail(ErrorKind.Conflict, "roadmap.has_submissions");
        (await App.Get<IRoadmapService>().RemoveTopicAsync(mentor.UserId, asg.RoadmapItemId, default)).ShouldFail(ErrorKind.Conflict, "roadmap.has_submissions");
        (await App.Get<IRoadmapService>().SetAssignmentActiveAsync(mentor.UserId, asg.AssignmentId, false, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Roadmap_operations_are_isolated_between_mentors_and_mentees()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var unrelatedMentee = await Data.MenteeAsync(b);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.QuizSingleChoice);
        var bTopic = await Data.TopicAsync(b);
        var bExercise = await Data.PublishedExerciseAsync(b, bTopic, ExerciseType.QuizSingleChoice);
        var svc = App.Get<IRoadmapService>();

        (await svc.SetCompetencyStatusAsync(b.UserId, asg.RoadmapItemId, new(CompetencyStatus.Learning, null), default)).ShouldBeNotFound();
        (await svc.SetCompetencyStatusAsync(unrelatedMentee.UserId, asg.RoadmapItemId, new(CompetencyStatus.Learning, null), default)).ShouldBeNotFound();
        (await svc.SetCompetencyStatusAsync(Guid.NewGuid(), asg.RoadmapItemId, new(CompetencyStatus.Learning, null), default)).ShouldBeNotFound();
        (await svc.GetForMentorAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await svc.CreateAsync(b.UserId, new(mentee.MentorshipId, "x", "x"), default)).ShouldBeNotFound();
        (await svc.UpdateDetailsAsync(b.UserId, asg.RoadmapId, new("x", "x"), default)).ShouldBeNotFound();
        (await svc.AddTopicAsync(b.UserId, new(asg.RoadmapId, bTopic, null), default)).ShouldBeNotFound();
        (await svc.AssignExerciseAsync(b.UserId, new(asg.RoadmapItemId, bExercise, true, null), default)).ShouldBeNotFound();
        (await svc.AssignExerciseAsync(a.UserId, new(asg.RoadmapItemId, bExercise, true, null), default)).ShouldBeNotFound();
        (await svc.AddTopicAsync(a.UserId, new(asg.RoadmapId, bTopic, null), default)).ShouldBeNotFound();
        (await svc.UpdateAssignmentAsync(b.UserId, asg.AssignmentId, new(false, null), default)).ShouldBeNotFound();
        (await svc.SetAssignmentActiveAsync(b.UserId, asg.AssignmentId, false, default)).ShouldBeNotFound();
        (await svc.SetTopicActiveAsync(b.UserId, asg.RoadmapItemId, false, default)).ShouldBeNotFound();
        (await svc.RemoveAssignmentAsync(b.UserId, asg.AssignmentId, default)).ShouldBeNotFound();
        (await svc.RemoveTopicAsync(b.UserId, asg.RoadmapItemId, default)).ShouldBeNotFound();
        (await svc.ReorderTopicsAsync(b.UserId, asg.RoadmapId, [asg.RoadmapItemId], default)).ShouldBeNotFound();
        (await svc.PublishAsync(b.UserId, asg.RoadmapId, default)).ShouldBeNotFound();
        (await svc.GetForMenteeAsync(unrelatedMentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await svc.GetForMenteeAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await svc.GetForMentorAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
    }
}
