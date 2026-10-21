using MentorPath.Application.Progress;

namespace MentorPath.UnitTests.Progress;

public sealed class RoadmapMapBranchTests
{
    private static readonly Guid Root = Guid.NewGuid(), Planned = Guid.NewGuid(), Ghost1 = Guid.NewGuid(),
        Ghost2 = Guid.NewGuid(), Archived = Guid.NewGuid(), Other = Guid.NewGuid(), OtherChild = Guid.NewGuid();

    private static MapCatalogTopic T(Guid id, Guid? parent, string title, bool archived = false) => new(id, parent, title, archived);

    [Fact]
    public void Only_descendants_of_active_plan_topics_are_returned_ordered_by_title()
    {
        var catalog = new[]
        {
            T(Root, null, "Root"), T(Ghost2, Root, "Zeta"), T(Ghost1, Root, "Alfa"),
            T(Other, null, "Other"), T(OtherChild, Other, "Foreign branch"),
        };

        var ghosts = RoadmapMapRules.UnplannedDescendants([Root], [Root], catalog);

        ghosts.Select(g => g.Id).ShouldBe([Ghost1, Ghost2]);
    }

    [Fact]
    public void Traversal_continues_through_planned_active_children_but_not_through_withdrawn_ones()
    {
        var catalog = new[] { T(Root, null, "Root"), T(Planned, Root, "Planned"), T(Ghost1, Planned, "Deep") };

        RoadmapMapRules.UnplannedDescendants([Root, Planned], [Root, Planned], catalog).Select(g => g.Id).ShouldBe([Ghost1]);
        RoadmapMapRules.UnplannedDescendants([Root], [Root, Planned], catalog).ShouldBeEmpty();
    }

    [Fact]
    public void Archived_topics_and_their_subtrees_are_hidden()
    {
        var catalog = new[] { T(Root, null, "Root"), T(Archived, Root, "Old", archived: true), T(Ghost1, Archived, "Under old") };

        RoadmapMapRules.UnplannedDescendants([Root], [Root], catalog).ShouldBeEmpty();
    }

    [Fact]
    public void Nearest_ancestor_skips_topics_that_are_not_on_the_map()
    {
        var parentOf = new Dictionary<Guid, Guid?> { [Root] = null, [Planned] = Root, [Ghost1] = Planned };

        RoadmapMapRules.NearestAncestorOnMap(Ghost1, parentOf, new HashSet<Guid> { Root }).ShouldBe(Root);
        RoadmapMapRules.NearestAncestorOnMap(Ghost1, parentOf, new HashSet<Guid> { Root, Planned }).ShouldBe(Planned);
        RoadmapMapRules.NearestAncestorOnMap(Ghost1, parentOf, new HashSet<Guid>()).ShouldBeNull();
        RoadmapMapRules.NearestAncestorOnMap(Root, parentOf, new HashSet<Guid> { Root }).ShouldBeNull();
    }
}
