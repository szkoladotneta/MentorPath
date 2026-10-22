using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Roadmaps;

public sealed class TopicPreparationTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IRoadmapService Roadmaps => App.Get<IRoadmapService>();
    private ITopicPreparationService Preparation => App.Get<ITopicPreparationService>();
    private ITopicResourceService Resources => App.Get<ITopicResourceService>();

    [Fact]
    public async Task Mentees_share_topic_resources_but_keep_personal_notes_and_only_assigned_exercises()
    {
        var mentor = await Data.MentorAsync();
        var firstMentee = await Data.MenteeAsync(mentor);
        var secondMentee = await Data.MenteeAsync(mentor);
        var topicId = await Data.TopicAsync(mentor);
        var assignedExerciseId = await Data.PublishedExerciseAsync(mentor, topicId, ExerciseType.ArchitecturalDecision);
        var inactiveExerciseId = await Data.PublishedExerciseAsync(mentor, topicId, ExerciseType.QuizSingleChoice);
        var first = await CreatePlacementAsync(mentor.UserId, firstMentee.MentorshipId, topicId, assignedExerciseId, "Notatka pierwsza");
        var second = await CreatePlacementAsync(mentor.UserId, secondMentee.MentorshipId, topicId, assignedExerciseId, "Notatka druga");
        var inactiveAssignmentId = (await Roadmaps.AssignExerciseAsync(mentor.UserId,
            new(first.RoadmapItemId, inactiveExerciseId, false, null), default)).ShouldSucceed();
        (await Roadmaps.SetAssignmentActiveAsync(mentor.UserId, inactiveAssignmentId, false, default)).ShouldSucceed();
        (await Resources.ReplaceAsync(mentor.UserId, topicId,
            [new("Przewodnik", "https://example.com/guide", TopicResourceKind.Article, "Materiał wspólny")], default)).ShouldSucceed();
        (await App.Get<ITopicService>().SetArchivedAsync(mentor.UserId, topicId, true, default)).ShouldSucceed();

        var firstView = (await Preparation.GetTopicForMenteeAsync(firstMentee.UserId, firstMentee.MentorshipId, first.RoadmapItemId, default)).ShouldSucceed();
        var secondView = (await Preparation.GetTopicForMenteeAsync(secondMentee.UserId, secondMentee.MentorshipId, second.RoadmapItemId, default)).ShouldSucceed();

        var topic = (await App.Get<ITopicService>().GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        firstView.TopicTitle.ShouldBe(topic.Title);
        firstView.TopicDescriptionMarkdown.ShouldBe("Opis tematu");
        firstView.MentorNoteMarkdown.ShouldBe("Notatka pierwsza");
        secondView.MentorNoteMarkdown.ShouldBe("Notatka druga");
        firstView.Resources.Select(r => r.Title).ShouldBe(["Przewodnik"]);
        secondView.Resources.Select(r => r.TopicResourceId).ShouldBe(firstView.Resources.Select(r => r.TopicResourceId));
        firstView.Assignments.Select(a => a.AssignmentId).ShouldBe([first.AssignmentId]);
        secondView.Assignments.Select(a => a.AssignmentId).ShouldBe([second.AssignmentId]);
        firstView.Assignments.ShouldAllBe(a => a.ExerciseTitle == "Decyzja");
    }

    [Fact]
    public async Task Contextual_read_rejects_unauthorized_and_mixed_context_ids()
    {
        var mentor = await Data.MentorAsync();
        var foreignMentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var sameWorkspaceMentee = await Data.MenteeAsync(mentor);
        var foreignMentee = await Data.MenteeAsync(foreignMentor);
        var secondContext = await Data.MenteeAsync(foreignMentor, mentee.Email, mentee.UserId);
        var ownPlacement = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var foreignPlacement = await Data.PublishedAssignmentAsync(foreignMentor, secondContext, ExerciseType.ArchitecturalDecision);
        var unknown = Guid.NewGuid();

        foreach (var actorId in new[]
                 {
                     mentor.UserId,
                     foreignMentor.UserId,
                     sameWorkspaceMentee.UserId,
                     foreignMentee.UserId,
                     unknown,
                 })
        {
            (await Preparation.GetTopicForMenteeAsync(actorId, mentee.MentorshipId, ownPlacement.RoadmapItemId, default)).ShouldBeNotFound();
        }

        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, foreignPlacement.RoadmapItemId, default)).ShouldBeNotFound();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, secondContext.MentorshipId, ownPlacement.RoadmapItemId, default)).ShouldBeNotFound();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, Guid.NewGuid(), ownPlacement.RoadmapItemId, default)).ShouldBeNotFound();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, Guid.NewGuid(), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Read_allows_paused_mentorship_but_rejects_closed_draft_and_inactive_contexts()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var draftMentee = await Data.MenteeAsync(mentor);
        var published = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var draft = await Data.PublishedAssignmentAsync(mentor, draftMentee, ExerciseType.QuizSingleChoice, publish: false);

        (await Preparation.GetTopicForMenteeAsync(draftMentee.UserId, draftMentee.MentorshipId, draft.RoadmapItemId, default)).ShouldBeNotFound();
        (await Roadmaps.SetTopicActiveAsync(mentor.UserId, published.RoadmapItemId, false, default)).ShouldSucceed();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, published.RoadmapItemId, default)).ShouldBeNotFound();
        (await Roadmaps.SetTopicActiveAsync(mentor.UserId, published.RoadmapItemId, true, default)).ShouldSucceed();
        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Paused, default)).ShouldSucceed();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, published.RoadmapItemId, default)).ShouldSucceed();

        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default)).ShouldSucceed();
        (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, published.RoadmapItemId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Resources_do_not_gate_submissions_or_change_roadmap_progress()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var progress = App.Get<IProgressService>();
        var before = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();

        (await Resources.ReplaceAsync(mentor.UserId, assignment.TopicId,
            [new("Materiały niewymagane", "https://example.com/optional", TopicResourceKind.Article, null)], default)).ShouldSucceed();
        var afterResources = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        afterResources.PercentComplete.ShouldBe(before.PercentComplete);
        afterResources.CompletedTopics.ShouldBe(before.CompletedTopics);
        afterResources.CountedTopics.ShouldBe(before.CountedTopics);
        afterResources.Topics.Select(t => t.Status).ShouldBe(before.Topics.Select(t => t.Status));

        await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var view = (await Preparation.GetTopicForMenteeAsync(mentee.UserId, mentee.MentorshipId, assignment.RoadmapItemId, default)).ShouldSucceed();
        view.Assignments.Single().LatestStatus.ShouldBe(SubmissionStatus.PendingReview);
        view.Assignments.Single().AttemptCount.ShouldBe(1);
    }

    private async Task<AssignmentRef> CreatePlacementAsync(Guid mentorId, Guid mentorshipId, Guid topicId, Guid exerciseId, string note)
    {
        var roadmapId = (await Roadmaps.CreateAsync(mentorId, new(mentorshipId, "Roadmapa", "Cel"), default)).ShouldSucceed();
        var itemId = (await Roadmaps.AddTopicAsync(mentorId, new(roadmapId, topicId, note), default)).ShouldSucceed();
        var assignmentId = (await Roadmaps.AssignExerciseAsync(mentorId, new(itemId, exerciseId, true, null), default)).ShouldSucceed();
        (await Roadmaps.PublishAsync(mentorId, roadmapId, default)).ShouldSucceed();
        return new AssignmentRef(roadmapId, itemId, assignmentId, exerciseId, topicId);
    }
}
