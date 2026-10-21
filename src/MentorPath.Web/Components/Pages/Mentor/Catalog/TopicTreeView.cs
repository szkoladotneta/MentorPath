using MentorPath.Application.Catalog.Contracts;

namespace MentorPath.Web.Components.Pages.Mentor.Catalog;

public static class TopicTreeView
{
    /// <summary>Depth-first order: each topic is followed by its sub-topics. Topics whose parent is not in the list are top-level.</summary>
    public static List<(TopicListItem Topic, int Depth)> Flatten(IReadOnlyList<TopicListItem> topics)
    {
        var ids = topics.Select(t => t.TopicId).ToHashSet();
        var childrenOf = topics.Where(t => t.ParentTopicId is { } p && ids.Contains(p) && p != t.TopicId)
            .ToLookup(t => t.ParentTopicId!.Value);
        var isChild = childrenOf.SelectMany(g => g).Select(t => t.TopicId).ToHashSet();
        var result = new List<(TopicListItem, int)>();
        var seen = new HashSet<Guid>();

        void Visit(TopicListItem topic, int depth)
        {
            if (!seen.Add(topic.TopicId)) return;
            result.Add((topic, depth));
            foreach (var child in childrenOf[topic.TopicId]) Visit(child, depth + 1);
        }

        foreach (var root in topics.Where(t => !isChild.Contains(t.TopicId))) Visit(root, 0);
        foreach (var rest in topics) Visit(rest, 0);
        return result;
    }

    /// <summary>Choices for the "parent topic" select: active topics in tree order, without the edited topic and its sub-topics.</summary>
    public static List<(Guid Id, string Label)> ParentOptions(IReadOnlyList<TopicListItem> topics, Guid? editedTopicId)
    {
        var options = new List<(Guid, string)>();
        int? skipBelow = null;
        foreach (var (topic, depth) in Flatten(topics))
        {
            if (skipBelow is { } d && depth > d) continue;
            skipBelow = null;
            if (topic.TopicId == editedTopicId) { skipBelow = depth; continue; }
            if (topic.IsArchived) continue;
            options.Add((topic.TopicId, string.Concat(Enumerable.Repeat("— ", depth)) + topic.Title));
        }
        return options;
    }
}
