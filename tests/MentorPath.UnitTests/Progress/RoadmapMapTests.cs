using MentorPath.Application.Progress;
using MentorPath.Application.Progress.Contracts;

namespace MentorPath.UnitTests.Progress;

public sealed class RoadmapMapTests
{
    [Fact]
    public void Withdrawn_item_is_withdrawn_regardless_of_assignments() =>
        RoadmapMapRules.StateOf(false, [(true, AssignmentProgressStatus.Completed)]).ShouldBe(MapNodeState.Withdrawn);

    [Theory]
    [InlineData(AssignmentProgressStatus.NotStarted, MapNodeState.NotStarted)]
    [InlineData(AssignmentProgressStatus.InProgress, MapNodeState.InProgress)]
    [InlineData(AssignmentProgressStatus.PendingReview, MapNodeState.PendingReview)]
    [InlineData(AssignmentProgressStatus.NeedsRevision, MapNodeState.NeedsRevision)]
    [InlineData(AssignmentProgressStatus.Completed, MapNodeState.Completed)]
    public void Single_required_assignment_maps_to_node_state(AssignmentProgressStatus status, MapNodeState expected) =>
        RoadmapMapRules.StateOf(true, [(true, status)]).ShouldBe(expected);

    [Fact]
    public void Needs_revision_wins_over_pending_review()
    {
        var state = RoadmapMapRules.StateOf(true, [(true, AssignmentProgressStatus.PendingReview), (true, AssignmentProgressStatus.NeedsRevision)]);
        state.ShouldBe(MapNodeState.NeedsRevision);
    }

    [Fact]
    public void Topic_without_required_assignments_is_never_completed() =>
        RoadmapMapRules.StateOf(true, [(false, AssignmentProgressStatus.Completed)]).ShouldBe(MapNodeState.InProgress);

    [Fact]
    public void Current_node_prefers_revision_then_first_unfinished_counted_node()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        RoadmapMapRules.CurrentNode([(a, MapNodeState.Completed, true), (b, MapNodeState.NotStarted, true), (c, MapNodeState.NeedsRevision, true)]).ShouldBe(c);
        RoadmapMapRules.CurrentNode([(a, MapNodeState.Completed, true), (b, MapNodeState.NotStarted, false), (c, MapNodeState.InProgress, true)]).ShouldBe(c);
        RoadmapMapRules.CurrentNode([(a, MapNodeState.Completed, true), (b, MapNodeState.Withdrawn, false)]).ShouldBeNull();
        RoadmapMapRules.CurrentNode([]).ShouldBeNull();
    }

    [Fact]
    public void Layout_places_top_level_nodes_left_to_right_and_joins_them()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var layout = RoadmapMapLayout.Compute(ids.Select(i => new MapLayoutInput(i, null)).ToList());

        layout.Nodes.Select(n => n.Key).ShouldBe(ids);
        layout.Nodes.Select(n => n.X).ShouldBe(layout.Nodes.Select(n => n.X).OrderBy(x => x));
        layout.Nodes.Select(n => n.Y).Distinct().ShouldHaveSingleItem();
        layout.Edges.Count.ShouldBe(2);
        layout.Edges.ShouldAllBe(e => !e.IsBranch);
    }

    [Fact]
    public void Layout_hangs_children_below_parent_with_indent_and_no_overlap()
    {
        var root = Guid.NewGuid();
        var child1 = Guid.NewGuid();
        var grandchild = Guid.NewGuid();
        var child2 = Guid.NewGuid();
        var next = Guid.NewGuid();
        var layout = RoadmapMapLayout.Compute([new(root, null), new(child1, root), new(grandchild, child1), new(child2, root), new(next, null)]);
        var at = layout.Nodes.ToDictionary(n => n.Key);

        at[child1].Y.ShouldBeGreaterThan(at[root].Y);
        at[child1].X.ShouldBe(at[root].X + RoadmapMapLayout.Indent);
        at[grandchild].X.ShouldBe(at[child1].X + RoadmapMapLayout.Indent);
        at[child2].Y.ShouldBeGreaterThan(at[grandchild].Y);
        at[next].X.ShouldBeGreaterThan(at[grandchild].X + RoadmapMapLayout.NodeWidth);
        layout.Edges.Count(e => e.IsBranch).ShouldBe(3);
        layout.Edges.Count(e => !e.IsBranch).ShouldBe(1);
        layout.Nodes.Select(n => (n.X, n.Y)).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void Layout_survives_unknown_parents_and_cycles()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        var layout = RoadmapMapLayout.Compute([new(a, b), new(b, a), new(orphan, Guid.NewGuid())]);

        layout.Nodes.Select(n => n.Key).ShouldBe([a, b, orphan], ignoreOrder: true);
    }

    [Fact]
    public void Layout_of_nothing_is_empty()
    {
        var layout = RoadmapMapLayout.Compute([]);
        layout.Nodes.ShouldBeEmpty();
        layout.Width.ShouldBe(0);
    }
}
