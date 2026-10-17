using MentorPath.Application.Progress.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Progress;

/// <summary>The single place for PRG-02..04 rules (D-16). Progress is always derived, never stored.</summary>
public static class ProgressCalculator
{
    public static AssignmentProgressStatus ForAssignment(IReadOnlyList<SubmissionStatus> statusesOldestFirst)
    {
        if (statusesOldestFirst.Count == 0) return AssignmentProgressStatus.NotStarted;
        if (statusesOldestFirst.Any(s => s is SubmissionStatus.Correct or SubmissionStatus.Accepted)) return AssignmentProgressStatus.Completed;
        return statusesOldestFirst[^1] switch
        {
            SubmissionStatus.PendingReview => AssignmentProgressStatus.PendingReview,
            SubmissionStatus.NeedsRevision => AssignmentProgressStatus.NeedsRevision,
            _ => AssignmentProgressStatus.InProgress,
        };
    }

    public static TopicProgressStatus ForTopic(IReadOnlyList<(bool IsRequired, AssignmentProgressStatus Status)> activeAssignments)
    {
        var required = activeAssignments.Where(a => a.IsRequired).ToList();
        if (required.Count > 0 && required.All(a => a.Status == AssignmentProgressStatus.Completed)) return TopicProgressStatus.Completed;
        return activeAssignments.Any(a => a.Status != AssignmentProgressStatus.NotStarted)
            ? TopicProgressStatus.InProgress
            : TopicProgressStatus.NotStarted;
    }

    public static bool CountsTowardsProgress(IReadOnlyList<(bool IsRequired, AssignmentProgressStatus Status)> activeAssignments) =>
        activeAssignments.Any(a => a.IsRequired);

    public static int Percent(int completed, int counted) => counted == 0 ? 0 : (int)Math.Floor(100.0 * completed / counted);
}
