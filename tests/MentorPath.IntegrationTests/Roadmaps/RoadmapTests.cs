using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
    public async Task Manual_demonstrated_status_requires_a_justification_and_is_visible_to_mentee_without_history()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var svc = App.Get<IRoadmapService>();
        var correctOptionId = await App.Db(ctx => ctx.QuizOptions.Where(o => o.ExerciseId == asg.ExerciseId && o.IsCorrect)
            .Select(o => o.Id).SingleAsync());
        (await App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId,
            new(asg.AssignmentId, Guid.NewGuid(), correctOptionId, "Rozumiem tę odpowiedź.", 4), default)).ShouldSucceed();

        (await svc.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, null), default))
            .ShouldFail(ErrorKind.Validation, "roadmap.competency_justification_required");
        (await svc.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new((CompetencyStatus)999, null), default))
            .ShouldFail(ErrorKind.Validation, "roadmap.invalid_competency_status");
        (await svc.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, new string('x', 1001)), default))
            .ShouldFail(ErrorKind.Validation, "roadmap.invalid_competency_justification");

        (await svc.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, "Mentee samodzielnie uzasadnił decyzję."), default)).ShouldSucceed();
        var mentorRoadmap = (await svc.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed()!;
        var item = mentorRoadmap.Items.Single(i => i.RoadmapItemId == asg.RoadmapItemId);
        item.Status.ShouldBe(CompetencyStatus.Demonstrated);
        item.StatusHistory!.Count.ShouldBe(1);
        item.StatusHistory[0].PreviousStatus.ShouldBe(CompetencyStatus.NotAssessed);
        item.StatusHistory[0].NewStatus.ShouldBe(CompetencyStatus.Demonstrated);
        item.StatusHistory[0].ChangedByUserId.ShouldBe(mentor.UserId);
        item.StatusHistory[0].ChangedAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);
        item.StatusHistory[0].ManualJustification.ShouldBe("Mentee samodzielnie uzasadnił decyzję.");
        (await svc.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, "Duplicate"), default)).ShouldSucceed();
        (await svc.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed()!
            .Items.Single(i => i.RoadmapItemId == asg.RoadmapItemId).StatusHistory!.Count.ShouldBe(1);

        var menteeRoadmap = (await svc.GetForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        menteeRoadmap.Items.Single(i => i.RoadmapItemId == asg.RoadmapItemId).Status.ShouldBe(CompetencyStatus.Demonstrated);
    }

    [Fact]
    public async Task Accepted_attempt_allows_demonstrated_status_without_manual_justification_and_preserves_history()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(submission.SubmissionId, "Mocne strony", "Rozwijaj dalej", "Jakie są kompromisy?", ReviewVerdict.Accepted,
                await Data.ScoresAsync(mentor)), default)).ShouldSucceed();

        (await Roadmaps.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, null), default)).ShouldSucceed();
        var item = (await Roadmaps.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed()!
            .Items.Single(i => i.RoadmapItemId == asg.RoadmapItemId);
        item.Evidence!.ShouldContain(e => e.SubmissionId == submission.SubmissionId && e.Status == SubmissionStatus.Accepted);
        item.StatusHistory!.Count.ShouldBe(1);

        (await Roadmaps.RemoveTopicAsync(mentor.UserId, asg.RoadmapItemId, default))
            .ShouldFail(ErrorKind.Conflict, "roadmap.has_submissions");
    }

    [Fact]
    public async Task Demonstrated_is_suggested_only_when_every_active_required_assignment_is_accepted()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var required = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var optionalExercise = await Data.PublishedExerciseAsync(mentor, required.TopicId, ExerciseType.PracticalTask);
        var secondRequiredExercise = await Data.PublishedExerciseAsync(mentor, required.TopicId, ExerciseType.ArchitecturalDecision);
        (await Roadmaps.AssignExerciseAsync(mentor.UserId,
            new(required.RoadmapItemId, optionalExercise, false, null), default)).ShouldSucceed();
        var secondRequiredId = (await Roadmaps.AssignExerciseAsync(mentor.UserId,
            new(required.RoadmapItemId, secondRequiredExercise, true, null), default)).ShouldSucceed();

        var firstSubmission = await Data.DecisionSubmissionAsync(mentee, required.AssignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(firstSubmission.SubmissionId, "Mocne strony", "Rozwijaj dalej", "Jakie są kompromisy?", ReviewVerdict.Accepted,
                await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        var item = (await Roadmaps.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed()!
            .Items.Single(i => i.RoadmapItemId == required.RoadmapItemId);
        item.SuggestedDemonstrated.ShouldBeFalse();
        item.Status.ShouldBe(CompetencyStatus.NotAssessed);

        var secondSubmission = await Data.DecisionSubmissionAsync(mentee, secondRequiredId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(secondSubmission.SubmissionId, "Mocne strony", "Rozwijaj dalej", "Jakie są kompromisy?", ReviewVerdict.Accepted,
                await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        item = (await Roadmaps.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed()!
            .Items.Single(i => i.RoadmapItemId == required.RoadmapItemId);
        item.SuggestedDemonstrated.ShouldBeTrue();
        item.Status.ShouldBe(CompetencyStatus.NotAssessed);
    }

    [Fact]
    public async Task Competency_status_history_prevents_removing_item_without_submissions()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        (await Roadmaps.SetCompetencyStatusAsync(mentor.UserId, asg.RoadmapItemId,
            new(CompetencyStatus.Learning, null), default)).ShouldSucceed();

        (await Roadmaps.RemoveTopicAsync(mentor.UserId, asg.RoadmapItemId, default))
            .ShouldFail(ErrorKind.Conflict, "roadmap.has_competency_history");
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
