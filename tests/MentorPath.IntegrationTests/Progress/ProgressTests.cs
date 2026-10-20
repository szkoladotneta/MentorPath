using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Progress;

public sealed class ProgressTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Topic_completes_when_all_active_required_assignments_are_completed_and_optional_ones_do_not_block()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var required = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var optionalExercise = await Data.PublishedExerciseAsync(mentor, required.TopicId, ExerciseType.PracticalTask);
        (await App.Get<IRoadmapService>().AssignExerciseAsync(mentor.UserId, new(required.RoadmapItemId, optionalExercise, false, null), default)).ShouldSucceed();
        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);

        var progress = App.Get<IProgressService>();
        var before = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        before.CountedTopics.ShouldBe(2);
        before.CompletedTopics.ShouldBe(0);
        before.Topics.ShouldAllBe(t => t.Status == TopicProgressStatus.NotStarted);

        var s = await Data.DecisionSubmissionAsync(mentee, required.AssignmentId);
        var pending = (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        pending.Topics.Single(t => t.RoadmapItemId == required.RoadmapItemId).Status.ShouldBe(TopicProgressStatus.InProgress);

        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        var after = (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        after.Topics.Single(t => t.RoadmapItemId == required.RoadmapItemId).Status.ShouldBe(TopicProgressStatus.Completed);
        after.CompletedTopics.ShouldBe(1);
        after.PercentComplete.ShouldBe(50);
        _ = second;
    }

    [Fact]
    public async Task Withdrawn_topics_are_excluded_and_progress_is_computed_not_stored()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var first = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var s = await Data.DecisionSubmissionAsync(mentee, first.AssignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, await Data.ScoresAsync(mentor)), default)).ShouldSucceed();
        (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().PercentComplete.ShouldBe(50);

        (await App.Get<IRoadmapService>().SetTopicActiveAsync(mentor.UserId, second.RoadmapItemId, false, default)).ShouldSucceed();
        var p = (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        p.CountedTopics.ShouldBe(1);
        p.PercentComplete.ShouldBe(100);
    }

    [Fact]
    public async Task Progress_and_dashboard_are_isolated()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);

        (await App.Get<IProgressService>().GetRoadmapProgressAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await App.Get<IProgressService>().GetRoadmapProgressAsync(other.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        var dashB = (await App.Get<IProgressService>().GetMentorDashboardAsync(b.UserId, default)).ShouldSucceed();
        dashB.Mentees.ShouldBeEmpty();
        dashB.PendingReviews.ShouldBeEmpty();
        dashB.RecentActivity.ShouldBeEmpty();

        var dashA = (await App.Get<IProgressService>().GetMentorDashboardAsync(a.UserId, default)).ShouldSucceed();
        dashA.Mentees.Count.ShouldBe(2);
        dashA.PendingReviews.Count.ShouldBe(1);
        dashA.RecentActivity.ShouldContain(x => x.Kind == ActivityKind.Submitted);
        dashA.Mentees.Single(m => m.MentorshipId == mentee.MentorshipId).PendingReviewCount.ShouldBe(1);
        (await App.Get<IProgressService>().GetMentorDashboardAsync(mentee.UserId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Mentee_sees_no_progress_before_roadmap_is_published()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice, publish: false);
        var p = (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        p.HasPublishedRoadmap.ShouldBeFalse();
        p.Topics.ShouldBeEmpty();
        (await App.Db(ctx => Task.FromResult(ctx.Model.GetEntityTypes().Any(t => t.ClrType.Name.Contains("Progress"))))).ShouldBeFalse();
    }
}
