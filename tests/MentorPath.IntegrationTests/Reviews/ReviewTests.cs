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
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(s.SubmissionId, "", "", "", ReviewVerdict.Accepted), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Concurrent_reviews_produce_exactly_one_feedback()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var s = await Data.DecisionSubmissionAsync(mentee, (await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision)).AssignmentId);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId,
                new(s.SubmissionId, "S", "I", "Q", i % 2 == 0 ? ReviewVerdict.Accepted : ReviewVerdict.NeedsRevision), default)));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => !r.IsSuccess).ShouldAllBe(r => r.Error!.Kind == ErrorKind.Conflict);
        (await App.Db(ctx => ctx.MentorFeedback.CountAsync(f => f.SubmissionId == s.SubmissionId))).ShouldBe(1);
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
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(first.SubmissionId, "S", "I", "Q", ReviewVerdict.NeedsRevision), default)).ShouldSucceed();
        (await App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId, new(first.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted), default))
            .ShouldFail(ErrorKind.Conflict, "review.not_pending");

        var second = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        second.AttemptNumber.ShouldBe(2);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(a.UserId, new(second.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted), default)).ShouldSucceed();

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
        (await Reviews.SubmitFeedbackAsync(a.UserId, new(s.SubmissionId, "S", "I", "Q", ReviewVerdict.Accepted), default)).ShouldSucceed();

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
