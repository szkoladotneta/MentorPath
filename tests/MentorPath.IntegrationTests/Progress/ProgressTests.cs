using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Components.Pages.Mentor.Insights;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Progress;

public sealed class ProgressTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public void Mentor_insights_duration_tiles_use_their_defined_units()
    {
        var thirtyMinutes = TimeSpan.FromMinutes(30);

        MentorInsightsDurationFormatter.FormatMinutes(thirtyMinutes).ShouldBe("30 min");
        MentorInsightsDurationFormatter.FormatHours(thirtyMinutes).ShouldBe("0,5 godz.");
        MentorInsightsDurationFormatter.FormatMinutes(null).ShouldBe("—");
        MentorInsightsDurationFormatter.FormatHours(null).ShouldBe("—");
    }

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
    public async Task Competency_progress_counts_active_roadmap_items_and_uses_floor_percentage()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var first = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var second = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var withdrawn = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var roadmaps = App.Get<IRoadmapService>();

        (await roadmaps.SetCompetencyStatusAsync(mentor.UserId, first.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, "Zaakceptowane rozwiązanie i uzasadnienie."), default)).ShouldSucceed();
        (await roadmaps.SetCompetencyStatusAsync(mentor.UserId, second.RoadmapItemId,
            new(CompetencyStatus.Learning, null), default)).ShouldSucceed();

        var progress = App.Get<IProgressService>();
        var third = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        third.CompetencyProgress.ShouldBe(new CompetencyProgressDto(1, 3, 33));
        third.CountedTopics.ShouldBe(3);

        (await roadmaps.SetTopicActiveAsync(mentor.UserId, withdrawn.RoadmapItemId, false, default)).ShouldSucceed();
        var half = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        half.CompetencyProgress.ShouldBe(new CompetencyProgressDto(1, 2, 50));

        (await roadmaps.SetCompetencyStatusAsync(mentor.UserId, second.RoadmapItemId,
            new(CompetencyStatus.Demonstrated, "Kompetencja potwierdzona podczas przeglądu."), default)).ShouldSucceed();
        var complete = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        complete.CompetencyProgress.ShouldBe(new CompetencyProgressDto(2, 2, 100));

        (await roadmaps.SetTopicActiveAsync(mentor.UserId, first.RoadmapItemId, false, default)).ShouldSucceed();
        (await roadmaps.SetTopicActiveAsync(mentor.UserId, second.RoadmapItemId, false, default)).ShouldSucceed();
        var empty = (await progress.GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        empty.CompetencyProgress.ShouldBe(new CompetencyProgressDto(0, 0, 0));
    }

    [Fact]
    public async Task Progress_and_dashboard_are_isolated()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        App.Clock.Advance(TimeSpan.FromDays(14));

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

        var insights = App.Get<IMentorInsightsService>();
        (await insights.GetMentorInsightsAsync(a.UserId, default)).ShouldSucceed()
            .AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(1, 1));
        (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(b.UserId, default)).ShouldSucceed()
            .AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(0, 0));
        (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(mentee.UserId, default)).ShouldBeNotFound();
        (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(Guid.NewGuid(), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Mentor_insights_calculate_mature_conversion_cohorts_and_median_durations()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, assignment.AssignmentId, default)).ShouldSucceed();

        App.Clock.Advance(TimeSpan.FromDays(14));
        var first = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, first.SubmissionId, default)).ShouldSucceed();
        var reviewDuration = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15);
        App.Clock.Advance(reviewDuration);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(first.SubmissionId, "Mocne strony", "Do poprawy", "Pytanie", ReviewVerdict.NeedsRevision, await Data.ScoresAsync(mentor)), default))
            .ShouldSucceed();

        var secondAssignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, secondAssignment.AssignmentId, default)).ShouldSucceed();
        var second = await Data.DecisionSubmissionAsync(mentee, secondAssignment.AssignmentId);
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, second.SubmissionId, default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromHours(1));
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(second.SubmissionId, "Mocne strony", "Do poprawy", "Pytanie", ReviewVerdict.Accepted, await Data.ScoresAsync(mentor)), default))
            .ShouldSucceed();

        App.Clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromHours(1));
        await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var insights = (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(mentor.UserId, default)).ShouldSucceed();

        insights.AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(1, 1));
        insights.MedianSubmittedToReviewed.ShouldBe(TimeSpan.FromMinutes(97.5));
        insights.NeedsRevisionToResubmittedWithin7Days.ShouldBe(new ConversionMetricDto(1, 1));
        insights.MedianReviewDuration.ShouldBe(TimeSpan.FromMinutes(97.5));
        insights.WindowStartUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime.AddDays(-30));
        insights.WindowEndUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Mentor_insights_include_exact_maturity_and_reporting_window_boundaries()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, assignment.AssignmentId, default)).ShouldSucceed();
        var insightsService = App.Get<IMentorInsightsService>();

        App.Clock.Advance(TimeSpan.FromDays(14));
        (await insightsService.GetMentorInsightsAsync(mentor.UserId, default)).ShouldSucceed()
            .AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(0, 1));

        App.Clock.Advance(TimeSpan.FromDays(16));
        (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(mentor.UserId, default)).ShouldSucceed()
            .AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(0, 1));

        App.Clock.Advance(TimeSpan.FromMicroseconds(1));
        (await App.Get<IMentorInsightsService>().GetMentorInsightsAsync(mentor.UserId, default)).ShouldSucceed()
            .AssignmentToSubmittedWithin14Days.ShouldBe(new ConversionMetricDto(0, 0));
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
