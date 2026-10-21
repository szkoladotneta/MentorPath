using MentorPath.Application.Catalog;

namespace MentorPath.UnitTests.Catalog;

public sealed class TopicTreeRulesTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    private static Dictionary<Guid, Guid?> Tree(params (Guid Id, Guid? Parent)[] rows) => rows.ToDictionary(r => r.Id, r => r.Parent);

    [Fact]
    public void Root_can_receive_a_child() =>
        TopicTreeRules.Check(Tree((A, null)), null, A).ShouldBe(TopicTreeViolation.None);

    [Fact]
    public void Topic_cannot_be_its_own_parent() =>
        TopicTreeRules.Check(Tree((A, null)), A, A).ShouldBe(TopicTreeViolation.Cycle);

    [Fact]
    public void Topic_cannot_move_below_its_own_descendant() =>
        TopicTreeRules.Check(Tree((A, null), (B, A), (C, B)), A, C).ShouldBe(TopicTreeViolation.Cycle);

    [Fact]
    public void Third_level_is_allowed_and_fourth_is_not()
    {
        var tree = Tree((A, null), (B, A), (C, B));
        TopicTreeRules.Check(tree, null, B).ShouldBe(TopicTreeViolation.None);
        TopicTreeRules.Check(tree, null, C).ShouldBe(TopicTreeViolation.TooDeep);
    }

    [Fact]
    public void Moving_a_subtree_counts_its_height()
    {
        // D has a child chain D -> C (height 2); putting D under B (depth 2) would make C level 4.
        var tree = Tree((A, null), (B, A), (D, null), (C, D));
        TopicTreeRules.Check(tree, D, B).ShouldBe(TopicTreeViolation.TooDeep);
        TopicTreeRules.Check(tree, D, A).ShouldBe(TopicTreeViolation.None);
    }

    [Fact]
    public void Existing_cycle_in_data_does_not_hang()
    {
        var tree = Tree((A, B), (B, A));
        TopicTreeRules.Check(tree, C, A).ShouldBe(TopicTreeViolation.None);
    }
}
