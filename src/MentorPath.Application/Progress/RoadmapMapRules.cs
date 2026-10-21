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

    /// <summary>
    /// Catalog sub-topics that hang below the plan but are not on the roadmap (D-45). Only descendants of active roadmap topics
    /// (through non-archived topics) qualify, so nothing unrelated to the mentee's plan is revealed. Ordered by title.
    /// </summary>
    public static IReadOnlyList<MapCatalogTopic> UnplannedDescendants(
        IReadOnlyCollection<Guid> activePlanTopics, IReadOnlyCollection<Guid> allPlanTopics, IReadOnlyList<MapCatalogTopic> catalog)
    {
        var childrenOf = catalog.Where(t => t.ParentId is not null && !t.IsArchived).ToLookup(t => t.ParentId!.Value);
        var result = new List<MapCatalogTopic>();
        var expanded = new HashSet<Guid>(activePlanTopics);
        var found = new HashSet<Guid>();
        var queue = new Queue<Guid>(activePlanTopics);
        while (queue.Count > 0)
        {
            foreach (var child in childrenOf[queue.Dequeue()])
            {
                if (allPlanTopics.Contains(child.Id))
                {
                    if (activePlanTopics.Contains(child.Id) && expanded.Add(child.Id)) queue.Enqueue(child.Id);
                }
                else if (found.Add(child.Id))
                {
                    result.Add(child);
                    queue.Enqueue(child.Id);
                }
            }
        }
        return result.OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id).ToList();
    }

    /// <summary>Nearest ancestor of <paramref name="topicId"/> that is shown on the map, or null when it is a top-level node.</summary>
    public static Guid? NearestAncestorOnMap(Guid topicId, IReadOnlyDictionary<Guid, Guid?> parentOf, IReadOnlySet<Guid> onMap)
    {
        var seen = new HashSet<Guid> { topicId };
        var current = parentOf.GetValueOrDefault(topicId);
        while (current is { } id && seen.Add(id))
        {
            if (onMap.Contains(id)) return id;
            current = parentOf.GetValueOrDefault(id);
        }
        return null;
    }
}

public sealed record MapCatalogTopic(Guid Id, Guid? ParentId, string Title, bool IsArchived);
