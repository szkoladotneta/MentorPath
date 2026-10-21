using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Progress;

public sealed class RoadmapMapTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Map_follows_submission_state_and_marks_the_first_unfinished_topic_as_current()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var first = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var maps = App.Get<IRoadmapMapService>();

        var initial = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        initial.HasRoadmap.ShouldBeTrue();
        initial.Nodes.Select(n => n.RoadmapItemId).ShouldBe([first.RoadmapItemId, second.RoadmapItemId]);
        initial.Nodes.ShouldAllBe(n => n.State == MapNodeState.NotStarted);
        initial.Nodes.Single(n => n.IsCurrent).RoadmapItemId.ShouldBe(first.RoadmapItemId);

        var submission = await Data.DecisionSubmissionAsync(mentee, first.AssignmentId);
        var pending = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        pending.Nodes[0].State.ShouldBe(MapNodeState.PendingReview);

        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(submission.SubmissionId, "S", "I", "Q", ReviewVerdict.NeedsRevision, await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        var revision = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        revision.Nodes[0].State.ShouldBe(MapNodeState.NeedsRevision);
        revision.Nodes[0].IsCurrent.ShouldBeTrue();

        var retry = await Data.DecisionSubmissionAsync(mentee, first.AssignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(retry.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        var done = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        done.Nodes[0].State.ShouldBe(MapNodeState.Completed);
        done.Nodes[0].CompletedRequired.ShouldBe(1);
        done.Nodes.Single(n => n.IsCurrent).RoadmapItemId.ShouldBe(second.RoadmapItemId);
        done.PercentComplete.ShouldBe(50);
    }

    [Fact]
    public async Task Mentee_does_not_see_a_draft_roadmap_or_withdrawn_topics_but_the_mentor_does()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var draft = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision, publish: false);
        var maps = App.Get<IRoadmapMapService>();

        (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().HasRoadmap.ShouldBeFalse();
        (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().Nodes.ShouldHaveSingleItem();

        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        (await App.Get<IRoadmapService>().SetTopicActiveAsync(mentor.UserId, draft.RoadmapItemId, false, default)).ShouldSucceed();

        var forMentee = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        forMentee.Nodes.ShouldHaveSingleItem().RoadmapItemId.ShouldBe(second.RoadmapItemId);
        var forMentor = (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        forMentor.Nodes.Single(n => n.RoadmapItemId == draft.RoadmapItemId).State.ShouldBe(MapNodeState.Withdrawn);
        forMentor.CountedTopics.ShouldBe(1);
    }

    [Fact]
    public async Task Mentorship_without_a_roadmap_returns_an_empty_map_not_an_error()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var maps = App.Get<IRoadmapMapService>();

        (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().HasRoadmap.ShouldBeFalse();
        (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().Nodes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Map_is_isolated_per_mentorship_and_role()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var otherMentee = await Data.MenteeAsync(a);
        await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        var maps = App.Get<IRoadmapMapService>();

        (await maps.GetMapForMenteeAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMenteeAsync(otherMentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMenteeAsync(Guid.NewGuid(), mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMenteeAsync(a.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMenteeAsync(mentee.UserId, Guid.NewGuid(), default)).ShouldBeNotFound();

        (await maps.GetMapForMentorAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMentorAsync(otherMentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMentorAsync(Guid.NewGuid(), mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMentorAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await maps.GetMapForMentorAsync(a.UserId, Guid.NewGuid(), default)).ShouldBeNotFound();
    }
}
