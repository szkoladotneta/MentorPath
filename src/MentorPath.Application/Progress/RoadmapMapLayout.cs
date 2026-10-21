using System.Globalization;

namespace MentorPath.Application.Progress;

public sealed record MapLayoutInput(Guid Key, Guid? ParentKey);

public sealed record MapLayoutNode(Guid Key, double X, double Y, int Depth);

/// <summary>A connector. <see cref="IsBranch"/> is a parent→child edge; otherwise it joins consecutive top-level nodes on the main path.</summary>
public sealed record MapLayoutEdge(Guid FromKey, Guid ToKey, bool IsBranch, string Path);

public sealed record MapLayout(double Width, double Height, IReadOnlyList<MapLayoutNode> Nodes, IReadOnlyList<MapLayoutEdge> Edges);

/// <summary>
/// Deterministic tree layout: top-level nodes run left to right along the main path (in input order),
/// descendants hang below their parent, indented per depth. Nodes with an unknown parent become top-level.
/// </summary>
public static class RoadmapMapLayout
{
    public const double NodeWidth = 200;
    public const double NodeHeight = 64;
    public const double GapX = 56;
    public const double GapY = 20;
    public const double Indent = 28;
    public const double Padding = 16;
    public const double TopMargin = 28;

    public static MapLayout Compute(IReadOnlyList<MapLayoutInput> inputs)
    {
        var keys = inputs.Select(i => i.Key).ToHashSet();
        var childrenOf = inputs
            .Where(i => i.ParentKey is { } p && keys.Contains(p) && p != i.Key)
            .GroupBy(i => i.ParentKey!.Value)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Key).ToList());
        var isChild = childrenOf.Values.SelectMany(v => v).ToHashSet();

        var visited = new HashSet<Guid>();
        var roots = new List<Guid>();
        foreach (var input in inputs.Where(i => !isChild.Contains(i.Key)))
            roots.Add(input.Key);
        // Nodes caught in a parent cycle are unreachable from any root; promote them so nothing is lost.
        var reachable = new HashSet<Guid>();
        foreach (var r in roots) Collect(r, childrenOf, reachable);
        foreach (var input in inputs.Where(i => !reachable.Contains(i.Key)).ToList())
        {
            if (reachable.Contains(input.Key)) continue;
            roots.Add(input.Key);
            Collect(input.Key, childrenOf, reachable);
        }

        var nodes = new List<MapLayoutNode>();
        var edges = new List<MapLayoutEdge>();
        var maxY = 0.0;
        var x = Padding;
        Guid? previousRoot = null;
        foreach (var root in roots)
        {
            var columnDepth = 0;
            var row = 0;
            Place(root, null, 0);
            if (previousRoot is { } prev)
            {
                var from = nodes.First(n => n.Key == prev);
                var to = nodes.First(n => n.Key == root);
                edges.Add(new MapLayoutEdge(prev, root, false,
                    $"M {F(from.X + NodeWidth)} {F(from.Y + NodeHeight / 2)} L {F(to.X)} {F(to.Y + NodeHeight / 2)}"));
            }
            previousRoot = root;
            x += NodeWidth + columnDepth * Indent + GapX;

            void Place(Guid key, MapLayoutNode? parent, int depth)
            {
                if (!visited.Add(key)) return;
                columnDepth = Math.Max(columnDepth, depth);
                var node = new MapLayoutNode(key, x + depth * Indent, TopMargin + Padding + row * (NodeHeight + GapY), depth);
                row++;
                nodes.Add(node);
                maxY = Math.Max(maxY, node.Y + NodeHeight);
                if (parent is not null)
                {
                    edges.Add(new MapLayoutEdge(parent.Key, key, true,
                        $"M {F(parent.X + Indent / 2)} {F(parent.Y + NodeHeight)} L {F(parent.X + Indent / 2)} {F(node.Y + NodeHeight / 2)} L {F(node.X)} {F(node.Y + NodeHeight / 2)}"));
                }
                if (childrenOf.TryGetValue(key, out var kids))
                    foreach (var kid in kids) Place(kid, node, depth + 1);
            }
        }

        var width = nodes.Count == 0 ? 0 : x - GapX + Padding;
        return new MapLayout(width, nodes.Count == 0 ? 0 : maxY + Padding, nodes, edges);
    }

    private static void Collect(Guid key, Dictionary<Guid, List<Guid>> childrenOf, HashSet<Guid> seen)
    {
        if (!seen.Add(key)) return;
        if (childrenOf.TryGetValue(key, out var kids))
            foreach (var kid in kids) Collect(kid, childrenOf, seen);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
