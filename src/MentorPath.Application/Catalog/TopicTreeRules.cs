namespace MentorPath.Application.Catalog;

internal enum TopicTreeViolation { None, Cycle, TooDeep }

/// <summary>Pure rules for the topic tree (D-45): one parent, no cycles, at most <see cref="MaxDepth"/> levels.</summary>
internal static class TopicTreeRules
{
    public const int MaxDepth = 3;

    /// <param name="parentOf">Current parent of every topic in the workspace.</param>
    /// <param name="topicId">The topic being moved, or null for a topic that does not exist yet.</param>
    /// <param name="newParent">The proposed parent.</param>
    public static TopicTreeViolation Check(IReadOnlyDictionary<Guid, Guid?> parentOf, Guid? topicId, Guid newParent)
    {
        var depthAbove = 0;
        var seen = new HashSet<Guid>();
        Guid? current = newParent;
        while (current is { } id && seen.Add(id))
        {
            if (id == topicId) return TopicTreeViolation.Cycle;
            depthAbove++;
            current = parentOf.GetValueOrDefault(id);
        }

        var height = topicId is { } moved ? HeightOf(parentOf, moved) : 1;
        return depthAbove + height > MaxDepth ? TopicTreeViolation.TooDeep : TopicTreeViolation.None;
    }

    private static int HeightOf(IReadOnlyDictionary<Guid, Guid?> parentOf, Guid root)
    {
        var childrenOf = parentOf.Where(p => p.Value is not null).ToLookup(p => p.Value!.Value, p => p.Key);
        var height = 0;
        var level = new List<Guid> { root };
        var seen = new HashSet<Guid> { root };
        while (level.Count > 0)
        {
            height++;
            level = level.SelectMany(id => childrenOf[id]).Where(seen.Add).ToList();
        }
        return height;
    }
}
