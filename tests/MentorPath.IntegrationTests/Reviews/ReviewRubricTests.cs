using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Reviews;

public sealed class ReviewRubricTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IReviewService Reviews => App.Get<IReviewService>();

    private async Task<List<RubricCriterionDto>> CriteriaAsync(MentorRef mentor) =>
        (await App.Get<IRubricService>().ListCriteriaAsync(mentor.UserId, true, default)).ShouldSucceed().ToList();

    private async Task<(MentorRef Mentor, MenteeRef Mentee, Guid SubmissionId, List<RubricCriterionDto> Criteria)> PendingAsync(IReadOnlyList<int>? selected = null)
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var criteria = await CriteriaAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision,
            criterionIds: selected?.Select(i => criteria[i].CriterionId).ToList());
        var s = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        return (mentor, mentee, s.SubmissionId, criteria);
    }

    private static SubmitFeedbackRequest Request(Guid submissionId, IReadOnlyList<CriterionScoreInput>? scores) =>
        new(submissionId, "S", "I", "Q", ReviewVerdict.Accepted, scores);

    private static List<CriterionScoreInput> Scores(IEnumerable<RubricCriterionDto> criteria, int score = 3) =>
        criteria.Select(c => new CriterionScoreInput(c.CriterionId, score, "")).ToList();

    private async Task AssertNothingStoredAsync(Guid submissionId)
    {
        (await App.Db(ctx => ctx.MentorFeedback.CountAsync(f => f.SubmissionId == submissionId))).ShouldBe(0);
        (await App.Db(ctx => ctx.FeedbackCriterionScores.CountAsync(x => x.Feedback!.SubmissionId == submissionId))).ShouldBe(0);
        (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == submissionId))).Status.ShouldBe(SubmissionStatus.PendingReview);
    }

    [Fact]
    public async Task Review_detail_lists_selected_active_criteria_for_the_mentor_only()
    {
        var (mentor, mentee, submissionId, criteria) = await PendingAsync([4, 1, 0]);
        var other = await Data.MentorAsync();

        var detail = (await Reviews.GetReviewDetailAsync(mentor.UserId, submissionId, default)).ShouldSucceed();
        detail.Criteria.ShouldNotBeNull().Select(c => c.CriterionId).ShouldBe([criteria[0].CriterionId, criteria[1].CriterionId, criteria[4].CriterionId]);
        detail.Criteria.ShouldAllBe(c => c.LevelDescriptions.Count == 5);

        (await App.Get<IRubricService>().SetActiveAsync(mentor.UserId, criteria[1].CriterionId, false, default)).ShouldSucceed();
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, submissionId, default)).ShouldSucceed()
            .Criteria!.Select(c => c.CriterionId).ShouldBe([criteria[0].CriterionId, criteria[4].CriterionId]);
        (await App.Get<IReviewService>().GetReviewDetailAsync(other.UserId, submissionId, default)).ShouldBeNotFound();
        (await App.Get<IReviewService>().GetReviewDetailAsync(mentee.UserId, submissionId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Review_requires_a_score_for_every_selected_criterion()
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0, 1, 2]);

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, null), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid").FieldErrors!.ShouldContainKey("Scores");
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, Request(submissionId, Scores(criteria.Take(2))), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid").FieldErrors!["Scores"].ShouldHaveSingleItem().ShouldContain(criteria[2].Name);
        await AssertNothingStoredAsync(submissionId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task Score_must_be_between_one_and_five(int score)
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0]);

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [new(criteria[0].CriterionId, score, "")]), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid").FieldErrors!.ShouldContainKey("Scores[0].Score");
        await AssertNothingStoredAsync(submissionId);
    }

    [Fact]
    public async Task Score_comment_is_limited_in_length()
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0]);

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [new(criteria[0].CriterionId, 3, new string('x', InputLimits.Markdown + 1))]), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid").FieldErrors!.ShouldContainKey("Scores[0].CommentMarkdown");
        await AssertNothingStoredAsync(submissionId);
    }

    [Fact]
    public async Task Duplicate_or_unselected_criterion_scores_are_rejected()
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0, 1]);

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [.. Scores(criteria.Take(2)), new(criteria[0].CriterionId, 4, "")]), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid");
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [.. Scores(criteria.Take(2)), new(criteria[2].CriterionId, 4, "")]), default))
            .ShouldFail(ErrorKind.Validation, "review.invalid");
        await AssertNothingStoredAsync(submissionId);
    }

    [Fact]
    public async Task Criterion_of_another_workspace_is_not_found_and_nothing_is_stored()
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0]);
        var other = await Data.MentorAsync();
        var foreign = await CriteriaAsync(other);

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [new(criteria[0].CriterionId, 3, ""), new(foreign[0].CriterionId, 3, "")]), default)).ShouldBeNotFound();
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, Request(submissionId, [new(foreign[0].CriterionId, 3, "")]), default)).ShouldBeNotFound();
        await AssertNothingStoredAsync(submissionId);
    }

    [Fact]
    public async Task Foreign_mentor_mentee_and_unknown_user_cannot_review_with_scores()
    {
        var (mentor, mentee, submissionId, criteria) = await PendingAsync();
        var other = await Data.MentorAsync();

        foreach (var actor in new[] { other.UserId, mentee.UserId, Guid.NewGuid() })
            (await App.Get<IReviewService>().SubmitFeedbackAsync(actor, Request(submissionId, Scores(criteria)), default)).ShouldBeNotFound();
        await AssertNothingStoredAsync(submissionId);
        _ = mentor;
    }

    [Fact]
    public async Task Scores_are_stored_with_feedback_and_shown_to_mentee_with_awarded_level_description()
    {
        var (mentor, mentee, submissionId, criteria) = await PendingAsync([2, 0]);
        var scores = new List<CriterionScoreInput> { new(criteria[2].CriterionId, 5, "  Świetnie  "), new(criteria[0].CriterionId, 2, "") };

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, scores), default)).ShouldSucceed();

        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == submissionId))).AssignmentId, default)).ShouldSucceed();
        var shown = history.Attempts.Single().Feedback!.Scores.ShouldNotBeNull();
        shown.Select(x => (x.CriterionId, x.Score, x.Position)).ShouldBe([
            (criteria[0].CriterionId, 2, criteria[0].Position),
            (criteria[2].CriterionId, 5, criteria[2].Position)]);
        shown[0].LevelDescription.ShouldBe(criteria[0].LevelDescriptions[1]);
        shown[1].LevelDescription.ShouldBe(criteria[2].LevelDescriptions[4]);
        shown[1].CommentMarkdown.ShouldBe("Świetnie");
        shown[0].Name.ShouldBe(criteria[0].Name);

        var mentorView = (await App.Get<IReviewService>().GetReviewDetailAsync(mentor.UserId, submissionId, default)).ShouldSucceed();
        mentorView.History.Attempts.Single().Feedback!.Scores!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deactivated_criterion_is_no_longer_required_but_earlier_scores_stay_visible()
    {
        var (mentor, mentee, first, criteria) = await PendingAsync([0, 1]);
        var assignmentId = (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == first))).AssignmentId;
        (await Reviews.SubmitFeedbackAsync(mentor.UserId, new(first, "S", "I", "Q", ReviewVerdict.NeedsRevision, Scores(criteria.Take(2), 2)), default)).ShouldSucceed();

        (await App.Get<IRubricService>().SetActiveAsync(mentor.UserId, criteria[1].CriterionId, false, default)).ShouldSucceed();
        var second = await Data.DecisionSubmissionAsync(mentee, assignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, Request(second.SubmissionId, Scores(criteria.Take(1), 4)), default)).ShouldSucceed();
        (await App.Db(ctx => ctx.FeedbackCriterionScores.CountAsync(x => x.Feedback!.SubmissionId == second.SubmissionId))).ShouldBe(1);

        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, assignmentId, default)).ShouldSucceed();
        history.Attempts.Single(a => a.SubmissionId == first).Feedback!.Scores!.Count.ShouldBe(2);
        history.Attempts.Single(a => a.SubmissionId == second.SubmissionId).Feedback!.Scores!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Review_without_active_selected_criteria_needs_no_scores_and_history_has_no_scores()
    {
        var (mentor, mentee, submissionId, criteria) = await PendingAsync([3]);
        (await App.Get<IRubricService>().SetActiveAsync(mentor.UserId, criteria[3].CriterionId, false, default)).ShouldSucceed();

        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, null), default)).ShouldSucceed();

        var assignmentId = (await App.Db(ctx => ctx.Submissions.SingleAsync(x => x.Id == submissionId))).AssignmentId;
        var feedback = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, assignmentId, default)).ShouldSucceed().Attempts.Single().Feedback!;
        feedback.Scores.ShouldBeNull();
        (await App.Db(ctx => ctx.FeedbackCriterionScores.CountAsync(x => x.Feedback!.SubmissionId == submissionId))).ShouldBe(0);
    }

    [Fact]
    public async Task Scores_cannot_be_added_to_feedback_that_already_exists()
    {
        var (mentor, _, submissionId, criteria) = await PendingAsync([0]);
        (await Reviews.SubmitFeedbackAsync(mentor.UserId, Request(submissionId, Scores(criteria.Take(1), 3)), default)).ShouldSucceed();

        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, Request(submissionId, Scores(criteria.Take(1), 5)), default))
            .ShouldFail(ErrorKind.Conflict, "review.not_pending");

        (await App.Db(ctx => ctx.FeedbackCriterionScores.Where(x => x.Feedback!.SubmissionId == submissionId).Select(x => (int)x.Score).ToListAsync())).ShouldBe([3]);
    }
}
