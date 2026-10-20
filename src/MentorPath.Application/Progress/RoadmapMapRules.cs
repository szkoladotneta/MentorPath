using MentorPath.Application.Progress.Contracts;

namespace MentorPath.Application.Progress;

/// <summary>Pure rules for the roadmap map. Completion itself still comes from <see cref="ProgressCalculator"/> (D-16).</summary>
public static class RoadmapMapRules
{
    public static MapNodeState StateOf(bool isActive, IReadOnlyList<(bool IsRequired, AssignmentProgressStatus Status)> activeAssignments)
    {
        if (!isActive) return MapNodeState.Withdrawn;
        if (ProgressCalculator.ForTopic(activeAssignments) == TopicProgressStatus.Completed) return MapNodeState.Completed;
        if (activeAssignments.Any(a => a.Status == AssignmentProgressStatus.NeedsRevision)) return MapNodeState.NeedsRevision;
        if (activeAssignments.Any(a => a.Status == AssignmentProgressStatus.PendingReview)) return MapNodeState.PendingReview;
        return ProgressCalculator.ForTopic(activeAssignments) == TopicProgressStatus.InProgress
            ? MapNodeState.InProgress
            : MapNodeState.NotStarted;
    }

    /// <summary>
    /// "You are here": the first node needing a revision, otherwise the first unfinished node that counts towards progress.
    /// Nodes must be ordered by roadmap position.
    /// </summary>
    public static Guid? CurrentNode(IReadOnlyList<(Guid Id, MapNodeState State, bool CountsTowardsProgress)> nodesInOrder)
    {
        var revision = nodesInOrder.FirstOrDefault(n => n.State == MapNodeState.NeedsRevision);
        if (revision != default) return revision.Id;
        var next = nodesInOrder.FirstOrDefault(n => n.CountsTowardsProgress && n.State != MapNodeState.Completed && n.State != MapNodeState.Withdrawn);
        return next == default ? null : next.Id;
    }
}
