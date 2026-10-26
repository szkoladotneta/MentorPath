using MentorPath.Application.Common;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Reviews;

public sealed class ReviewTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IReviewService Reviews => App.Get<IReviewService>();

    [Fact]
    public async Task Queue_contains_only_pending_submissions_of_own_workspace_in_fifo_order()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var m1 = await Data.MenteeAsync(a);
        var m2 = await Data.MenteeAsync(a);
        var mb = await Data.MenteeAsync(b);
        var s1 = await Data.DecisionSubmissionAsync(m1, (await Data.PublishedAssignmentAsync(a, m1, ExerciseType.ArchitecturalDecision)).AssignmentId);
        App.Clock.Advance(TimeSpan.FromMinutes(5));
        var s2 = await Data.DecisionSubmissionAsync(m2, (await Data.PublishedAssignmentAsync(a, m2, ExerciseType.ArchitecturalDecision)).AssignmentId);
        await Data.DecisionSubmissionAsync(mb, (await Data.PublishedAssignmentAsync(b, mb, ExerciseType.ArchitecturalDecision)).AssignmentId);

        var queue = (await Reviews.GetPendingAsync(a.UserId, default)).ShouldSucceed();
        queue.Select(q => q.SubmissionId).ShouldBe([s1.SubmissionId, s2.SubmissionId]);
        (await App.Get<IReviewService>().GetPendingAsync(b.UserId, default)).ShouldSucceed().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Review_detail_shows_rubric_and_mentor_guide_only_to_owner()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var s = await Data.DecisionSubmissionAsync(mentee, (await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision)).AssignmentId);

        var detail = (await Reviews.GetReviewDetailAsync(a.UserId, s.SubmissionId, default)).ShouldSucceed();
        detail.Exercise.Content.RubricMarkdown.ShouldBe("Kryteria");
        detail.Exercise.Content.MentorGuideMarkdown.ShouldBe("Guide AD");
        detail.History.Attempts.Count.ShouldBe(1);
        (await App.Get<IReviewService>().GetReviewDetailAsync(b.UserId, s.SubmissionId, default)).ShouldBeNotFound();
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentee.UserId, s.SubmissionId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Review_open_and_submitted_events_capture_duration_and_flag_long_reviews()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var reviews = App.Get<IReviewService>();

        (await reviews.GetReviewDetailAsync(mentor.UserId, submission.SubmissionId, default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromMinutes(1));
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, submission.SubmissionId, default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15));
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(submission.SubmissionId, "Mocne strony", "Do poprawy", "Pytanie", ReviewVerdict.NeedsRevision, await Data.ScoresAsync(mentor)), default))
            .ShouldSucceed();

        var events = await App.Db(ctx => ctx.ActivityEvents
            .Where(e => e.SubmissionId == submission.SubmissionId)
            .OrderBy(e => e.EventType)
            .ToListAsync());
        events.Count.ShouldBe(2);
        var opened = events.Single(e => e.EventType == ActivityEventType.ReviewOpened);
        opened.WorkspaceId.ShouldBe(mentor.WorkspaceId);
        opened.AssignmentId.ShouldBe(assignment.AssignmentId);
        opened.MenteeUserId.ShouldBe(mentee.UserId);
        opened.MentorUserId.ShouldBe(mentor.UserId);
        opened.OccurredAtUtc.ShouldBe(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));

        var submitted = events.Single(e => e.EventType == ActivityEventType.ReviewSubmitted);
        var submittedDuration = submitted.ReviewDuration.GetValueOrDefault();
        submittedDuration.ShouldBe(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(16));
        submitted.ExceedsTwoHours.ShouldBeTrue();
        submitted.OccurredAtUtc.ShouldBe(opened.OccurredAtUtc + submittedDuration);
    }

    [Fact]
    public async Task Review_of_exactly_two_hours_is_not_flagged_as_exceeding_the_threshold()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);

        (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, submission.SubmissionId, default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromHours(2));
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(submission.SubmissionId, "Mocne strony", "Do poprawy", "Pytanie", ReviewVerdict.Accepted, await Data.ScoresAsync(mentor)), default))
            .ShouldSucceed();

        var submitted = await App.Db(ctx => ctx.ActivityEvents.SingleAsync(e =>
            e.SubmissionId == submission.SubmissionId && e.EventType == ActivityEventType.ReviewSubmitted));
        submitted.ReviewDuration.ShouldBe(TimeSpan.FromHours(2));
        submitted.ExceedsTwoHours.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_workspace_owner_can_give_feedback()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var s = await Data.DecisionSubmissionAsync(mentee, (await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision)).AssignmentId);

        (await Reviews.SubmitFeedbackAsync(b.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted), default)).ShouldBeNotFound();
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentee.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted), default)).ShouldBeNotFound();
        (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == s.SubmissionId))).Status.ShouldBe(SubmissionStatus.PendingReview);
    }

    [Fact]
    public async Task Feedback_requires_all_sections()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var s = await Data.DecisionSubmissionAsync(mentee, (await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision)).AssignmentId);
        var scores = await Data.ScoresAsync(a);
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(s.SubmissionId, "", "", "", ReviewVerdict.Accepted, scores), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.Count.ShouldBe(3);
        (await App.Db(ctx => ctx.ActivityEvents.CountAsync(e =>
            e.SubmissionId == s.SubmissionId && e.EventType == ActivityEventType.ReviewSubmitted))).ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_reviews_produce_exactly_one_feedback()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var s = await Data.DecisionSubmissionAsync(mentee, (await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision)).AssignmentId);
        var scores = await Data.ScoresAsync(a);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId,
                new(s.SubmissionId, "S", "I", "Q", i % 2 == 0 ? ReviewVerdict.Accepted : ReviewVerdict.NeedsRevision, scores), default)));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => !r.IsSuccess).ShouldAllBe(r => r.Error!.Kind == ErrorKind.Conflict);
        (await App.Db(ctx => ctx.MentorFeedback.CountAsync(f => f.SubmissionId == s.SubmissionId))).ShouldBe(1);
        (await App.Db(ctx => ctx.FeedbackCriterionScores.CountAsync(x => x.Feedback!.SubmissionId == s.SubmissionId))).ShouldBe(scores.Count);
        var feedback = await App.Db(ctx => ctx.MentorFeedback.SingleAsync(f => f.SubmissionId == s.SubmissionId));
        var status = (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == s.SubmissionId))).Status;
        status.ShouldBe(feedback.Verdict == ReviewVerdict.Accepted ? SubmissionStatus.Accepted : SubmissionStatus.NeedsRevision);
    }

    [Fact]
    public async Task Needs_revision_allows_new_attempt_and_accepted_completes_assignment()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        var first = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        var scores = await Data.ScoresAsync(a);
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(first.SubmissionId, "S", "I", "Q", ReviewVerdict.NeedsRevision, scores), default)).ShouldSucceed();
        (await App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId, new(first.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, scores), default))
            .ShouldFail(ErrorKind.Conflict, "review.not_pending");

        var second = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        second.AttemptNumber.ShouldBe(2);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId, new(second.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, scores), default)).ShouldSucceed();

        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed()
            .SubmitBlockReason.ShouldBe(SubmitBlockReason.AlreadyCompleted);
    }

    [Fact]
    public async Task Reflection_rules()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        var s = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        var reflections = App.Get<IReflectionService>();

        (await reflections.SaveReflectionAsync(mentee.UserId, new(s.SubmissionId, 4, "Wniosek"), default)).ShouldFail(ErrorKind.Conflict, "reflection.not_reviewed");
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted, await Data.ScoresAsync(a)), default)).ShouldSucceed();

        (await App.Get<IReflectionService>().SaveReflectionAsync(other.UserId, new(s.SubmissionId, 4, "x"), default)).ShouldBeNotFound();
        (await App.Get<IReflectionService>().SaveReflectionAsync(a.UserId, new(s.SubmissionId, 4, "x"), default)).ShouldBeNotFound();
        (await App.Get<IReflectionService>().SaveReflectionAsync(mentee.UserId, new(s.SubmissionId, 9, ""), default)).ShouldFail(ErrorKind.Validation);
        (await App.Get<IReflectionService>().SaveReflectionAsync(mentee.UserId, new(s.SubmissionId, 5, "Zrozumiałem kompromisy"), default)).ShouldSucceed();
        (await App.Get<IReflectionService>().SaveReflectionAsync(mentee.UserId, new(s.SubmissionId, 2, "Jeszcze raz"), default)).ShouldFail(ErrorKind.Conflict, "reflection.already_exists");

        var item = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed().Attempts.Single();
        item.Reflection!.ConfidenceAfter.ShouldBe(5);
        item.CanAddReflection.ShouldBeFalse();
    }
}
