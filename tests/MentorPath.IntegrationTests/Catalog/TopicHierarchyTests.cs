using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Catalog;

public sealed class TopicHierarchyTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private ITopicService Topics => App.Get<ITopicService>();

    private static UpsertTopicRequest Req(string title, Guid? parent = null)
    {
        var slug = "t-" + Guid.NewGuid().ToString("N")[..12];
        return new(title, slug, "Architektura", Difficulty.Intermediate, "Opis", "- Cel", parent);
    }

    private async Task<Guid> CreateAsync(MentorRef mentor, string title, Guid? parent = null) =>
        (await Topics.CreateAsync(mentor.UserId, Req(title, parent), default)).ShouldSucceed();

    [Fact]
    public async Task Sub_topic_keeps_its_parent_and_is_listed_with_it()
    {
        var mentor = await Data.MentorAsync();
        var root = await CreateAsync(mentor, "Dane");
        var child = await CreateAsync(mentor, "Outbox", root);

        (await Topics.GetAsync(mentor.UserId, child, default)).ShouldSucceed().ParentTopicId.ShouldBe(root);
        var list = (await Topics.ListAsync(mentor.UserId, false, default)).ShouldSucceed();
        list.Single(t => t.TopicId == child).ParentTopicId.ShouldBe(root);
        list.Single(t => t.TopicId == root).ParentTopicId.ShouldBeNull();

        (await Topics.UpdateAsync(mentor.UserId, child, Req("Outbox"), default)).ShouldSucceed();
        (await Topics.GetAsync(mentor.UserId, child, default)).ShouldSucceed().ParentTopicId.ShouldBeNull();
    }

    [Fact]
    public async Task Parent_from_another_workspace_or_unknown_is_not_found()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var foreignParent = await CreateAsync(b, "Obcy");
        var own = await CreateAsync(a, "Własny");

        (await Topics.CreateAsync(a.UserId, Req("X", foreignParent), default)).ShouldBeNotFound();
        (await Topics.CreateAsync(a.UserId, Req("X", Guid.NewGuid()), default)).ShouldBeNotFound();
        (await Topics.UpdateAsync(a.UserId, own, Req("Własny", foreignParent), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Foreign_mentor_mentee_and_unknown_user_cannot_attach_to_a_parent()
    {
        var owner = await Data.MentorAsync();
        var foreign = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(owner);
        var parent = await CreateAsync(owner, "Dane");
        var child = await CreateAsync(owner, "Outbox");

        (await Topics.CreateAsync(foreign.UserId, Req("X", parent), default)).ShouldBeNotFound();
        (await Topics.CreateAsync(mentee.UserId, Req("X", parent), default)).ShouldBeNotFound();
        (await Topics.CreateAsync(Guid.NewGuid(), Req("X", parent), default)).ShouldBeNotFound();
        (await Topics.UpdateAsync(foreign.UserId, child, Req("Outbox", parent), default)).ShouldBeNotFound();
        (await Topics.UpdateAsync(mentee.UserId, child, Req("Outbox", parent), default)).ShouldBeNotFound();
        (await Topics.UpdateAsync(Guid.NewGuid(), child, Req("Outbox", parent), default)).ShouldBeNotFound();
        (await Topics.GetAsync(owner.UserId, child, default)).ShouldSucceed().ParentTopicId.ShouldBeNull();
    }

    [Fact]
    public async Task Cycles_and_trees_deeper_than_three_levels_are_rejected()
    {
        var mentor = await Data.MentorAsync();
        var l1 = await CreateAsync(mentor, "L1");
        var l2 = await CreateAsync(mentor, "L2", l1);
        var l3 = await CreateAsync(mentor, "L3", l2);

        (await Topics.CreateAsync(mentor.UserId, Req("L4", l3), default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_parent");
        (await Topics.UpdateAsync(mentor.UserId, l1, Req("L1", l1), default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_parent");
        var cycle = (await Topics.UpdateAsync(mentor.UserId, l1, Req("L1", l3), default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_parent");
        cycle.FieldErrors!.ShouldContainKey("ParentTopicId");

        var other = await CreateAsync(mentor, "Inny korzeń");
        var otherChild = await CreateAsync(mentor, "Inny potomek", other);
        (await Topics.UpdateAsync(mentor.UserId, other, Req("Inny korzeń", l2), default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_parent");
        (await Topics.UpdateAsync(mentor.UserId, otherChild, Req("Inny potomek", l2), default)).ShouldSucceed();
    }

    [Fact]
    public async Task Archived_topic_cannot_become_a_parent_but_existing_children_keep_working()
    {
        var mentor = await Data.MentorAsync();
        var parent = await CreateAsync(mentor, "Dane");
        var child = await CreateAsync(mentor, "Outbox", parent);
        (await Topics.SetArchivedAsync(mentor.UserId, parent, true, default)).ShouldSucceed();

        (await Topics.CreateAsync(mentor.UserId, Req("Nowy", parent), default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_parent");
        (await Topics.UpdateAsync(mentor.UserId, child, Req("Outbox v2", parent), default)).ShouldSucceed();
    }

    [Fact]
    public async Task Map_shows_unplanned_sub_topics_as_title_only_ghosts_and_mentor_can_add_them()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var planned = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var ghost = await CreateAsync(mentor, "Outbox", planned.TopicId);
        var deepGhost = await CreateAsync(mentor, "Saga", ghost);
        var unrelatedRoot = await CreateAsync(mentor, "Poza gałęzią");
        await CreateAsync(mentor, "Dziecko poza gałęzią", unrelatedRoot);
        var maps = App.Get<IRoadmapMapService>();

        foreach (var map in new[]
        {
            (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed(),
            (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed(),
        })
        {
            map.Nodes.Select(n => n.TopicId).ShouldBe([planned.TopicId, ghost, deepGhost]);
            var node = map.Nodes.Single(n => n.TopicId == ghost);
            node.State.ShouldBe(MapNodeState.NotInPlan);
            node.ParentTopicId.ShouldBe(planned.TopicId);
            node.RoadmapItemId.ShouldBe(Guid.Empty);
            node.Assignments.ShouldBeEmpty();
            map.Nodes.Single(n => n.TopicId == deepGhost).ParentTopicId.ShouldBe(ghost);
            map.CountedTopics.ShouldBe(1);
        }

        (await App.Get<IRoadmapService>().AddTopicAsync(mentor.UserId, new(planned.RoadmapId, ghost, null), default)).ShouldSucceed();
        var after = (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        after.Nodes.Single(n => n.TopicId == ghost).State.ShouldNotBe(MapNodeState.NotInPlan);
        after.Nodes.Single(n => n.TopicId == ghost).RoadmapItemId.ShouldNotBe(Guid.Empty);
        after.Nodes.Single(n => n.TopicId == deepGhost).State.ShouldBe(MapNodeState.NotInPlan);
        after.CountedTopics.ShouldBe(1);
    }

    [Fact]
    public async Task Archived_sub_topics_are_not_shown_as_ghosts()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var planned = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var child = await CreateAsync(mentor, "Do archiwum", planned.TopicId);
        (await Topics.SetArchivedAsync(mentor.UserId, child, true, default)).ShouldSucceed();

        var map = (await App.Get<IRoadmapMapService>().GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        map.Nodes.ShouldHaveSingleItem().TopicId.ShouldBe(planned.TopicId);
    }

    [Fact]
    public async Task Withdrawn_child_and_its_unplanned_subtree_are_not_shown_to_the_mentee_as_ghosts()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var root = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var withdrawn = await CreateAsync(mentor, "Wycofany", root.TopicId);
        var below = await CreateAsync(mentor, "Pod wycofanym", withdrawn);
        var roadmaps = App.Get<IRoadmapService>();
        var itemId = (await roadmaps.AddTopicAsync(mentor.UserId, new(root.RoadmapId, withdrawn, null), default)).ShouldSucceed();
        (await roadmaps.SetTopicActiveAsync(mentor.UserId, itemId, false, default)).ShouldSucceed();
        var maps = App.Get<IRoadmapMapService>();

        var forMentee = (await maps.GetMapForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        forMentee.Nodes.ShouldHaveSingleItem().TopicId.ShouldBe(root.TopicId);

        var forMentor = (await maps.GetMapForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        forMentor.Nodes.Select(n => n.TopicId).ShouldBe([root.TopicId, withdrawn]);
        forMentor.Nodes.Select(n => n.TopicId).ShouldNotContain(below);
    }
}
