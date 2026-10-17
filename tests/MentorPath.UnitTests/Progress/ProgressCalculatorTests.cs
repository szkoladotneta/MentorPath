using MentorPath.Application.Progress;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.Progress;

public sealed class ProgressCalculatorTests
{
    [Theory]
    [InlineData(new SubmissionStatus[0], AssignmentProgressStatus.NotStarted)]
    [InlineData(new[] { SubmissionStatus.Incorrect }, AssignmentProgressStatus.InProgress)]
    [InlineData(new[] { SubmissionStatus.Incorrect, SubmissionStatus.Correct }, AssignmentProgressStatus.Completed)]
    [InlineData(new[] { SubmissionStatus.PendingReview }, AssignmentProgressStatus.PendingReview)]
    [InlineData(new[] { SubmissionStatus.NeedsRevision }, AssignmentProgressStatus.NeedsRevision)]
    [InlineData(new[] { SubmissionStatus.NeedsRevision, SubmissionStatus.PendingReview }, AssignmentProgressStatus.PendingReview)]
    [InlineData(new[] { SubmissionStatus.NeedsRevision, SubmissionStatus.Accepted }, AssignmentProgressStatus.Completed)]
    public void Assignment_status(SubmissionStatus[] statuses, AssignmentProgressStatus expected) =>
        ProgressCalculator.ForAssignment(statuses).ShouldBe(expected);

    [Fact]
    public void Topic_completes_when_all_required_are_completed_regardless_of_optional()
    {
        ProgressCalculator.ForTopic([(true, AssignmentProgressStatus.Completed), (false, AssignmentProgressStatus.NotStarted)])
            .ShouldBe(TopicProgressStatus.Completed);
        ProgressCalculator.ForTopic([(true, AssignmentProgressStatus.Completed), (true, AssignmentProgressStatus.PendingReview)])
            .ShouldBe(TopicProgressStatus.InProgress);
        ProgressCalculator.ForTopic([(true, AssignmentProgressStatus.NotStarted)]).ShouldBe(TopicProgressStatus.NotStarted);
    }

    [Fact]
    public void Topic_without_required_assignments_does_not_count()
    {
        List<(bool, AssignmentProgressStatus)> onlyOptional = [(false, AssignmentProgressStatus.Completed)];
        ProgressCalculator.CountsTowardsProgress(onlyOptional).ShouldBeFalse();
        ProgressCalculator.ForTopic(onlyOptional).ShouldBe(TopicProgressStatus.InProgress);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 3, 33)]
    [InlineData(2, 3, 66)]
    [InlineData(3, 3, 100)]
    public void Percent_is_floored(int completed, int counted, int expected) => ProgressCalculator.Percent(completed, counted).ShouldBe(expected);
}
