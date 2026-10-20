using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Catalog;

public sealed class RubricTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IRubricService Rubric => App.Get<IRubricService>();

    private static UpdateRubricCriterionRequest Valid(string name = "Nowa nazwa") =>
        new(name, ["Poziom 1", "Poziom 2", "Poziom 3", "Poziom 4", "Poziom 5"]);

    [Fact]
    public async Task New_workspace_gets_six_default_criteria_and_each_workspace_has_its_own()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();

        var list = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();
        list.Count.ShouldBe(6);
        list.Select(c => c.Position).ShouldBe([0, 1, 2, 3, 4, 5]);
        list.ShouldAllBe(c => c.IsActive && c.LevelDescriptions.Count == 5 && c.LevelDescriptions.All(d => d.Length > 0));

        var other = (await App.Get<IRubricService>().ListCriteriaAsync(b.UserId, false, default)).ShouldSucceed();
        other.Count.ShouldBe(6);
        other.Select(c => c.CriterionId).Intersect(list.Select(c => c.CriterionId)).ShouldBeEmpty();
        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Count.ShouldBe(6);
    }

    [Fact]
    public async Task Concurrent_first_access_seeds_exactly_one_set()
    {
        var a = await Data.MentorAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)));

        results.ShouldAllBe(r => r.IsSuccess);
        (await App.Db(ctx => ctx.RubricCriteria.CountAsync(c => c.WorkspaceId == a.WorkspaceId))).ShouldBe(6);
    }

    [Fact]
    public async Task Concurrent_deactivation_never_leaves_a_workspace_without_active_criteria()
    {
        var a = await Data.MentorAsync();
        var criteria = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();
        foreach (var c in criteria.Skip(2)) (await Rubric.SetActiveAsync(a.UserId, c.CriterionId, false, default)).ShouldSucceed();

        var results = await Task.WhenAll(criteria.Take(2).Select(c => App.Get<IRubricService>().SetActiveAsync(a.UserId, c.CriterionId, false, default)));

        results.Count(r => r.IsSuccess).ShouldBe(1);
        (await App.Db(ctx => ctx.RubricCriteria.CountAsync(c => c.WorkspaceId == a.WorkspaceId && c.IsActive))).ShouldBe(1);
    }

    [Fact]
    public async Task Mentee_and_unknown_user_cannot_list_criteria()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);

        (await App.Get<IRubricService>().ListCriteriaAsync(mentee.UserId, false, default)).ShouldBeNotFound();
        (await App.Get<IRubricService>().ListCriteriaAsync(Guid.NewGuid(), true, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Update_changes_name_and_level_descriptions()
    {
        var a = await Data.MentorAsync();
        var first = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed()[0];

        (await Rubric.UpdateCriterionAsync(a.UserId, first.CriterionId, Valid("  Moje kryterium "), default)).ShouldSucceed();

        var updated = (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Single(c => c.CriterionId == first.CriterionId);
        updated.Name.ShouldBe("Moje kryterium");
        updated.LevelDescriptions.ShouldBe(["Poziom 1", "Poziom 2", "Poziom 3", "Poziom 4", "Poziom 5"]);
        updated.Position.ShouldBe(first.Position);
    }

    [Fact]
    public async Task Update_validates_name_and_five_level_descriptions()
    {
        var a = await Data.MentorAsync();
        var first = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed()[0];

        (await Rubric.UpdateCriterionAsync(a.UserId, first.CriterionId, Valid(" "), default)).ShouldFail(ErrorKind.Validation, "rubric.invalid")
            .FieldErrors!.ShouldContainKey("Name");
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, first.CriterionId, Valid(new string('x', 101)), default)).ShouldFail(ErrorKind.Validation);
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, first.CriterionId, new("Ok", ["1", "2", "3", "4"]), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.ShouldContainKey("LevelDescriptions");
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, first.CriterionId, new("Ok", ["1", "2", " ", "4", "5"]), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.ShouldContainKey("LevelDescriptions[2]");
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, first.CriterionId, new("Ok", ["1", "2", "3", "4", new string('x', 1001)]), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.ShouldContainKey("LevelDescriptions[4]");

        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed()[0].Name.ShouldBe(first.Name);
    }

    [Fact]
    public async Task Criterion_name_is_unique_per_workspace_but_reusable_across_workspaces()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var criteria = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();

        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, criteria[1].CriterionId, Valid(criteria[0].Name), default))
            .ShouldFail(ErrorKind.Conflict, "rubric.name_taken");
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, criteria[0].CriterionId, Valid("Nazwa własna"), default)).ShouldSucceed();
        var foreign = (await App.Get<IRubricService>().ListCriteriaAsync(b.UserId, false, default)).ShouldSucceed();
        (await App.Get<IRubricService>().UpdateCriterionAsync(b.UserId, foreign[1].CriterionId, Valid("Nazwa własna"), default)).ShouldSucceed();
    }

    [Fact]
    public async Task Criteria_can_be_deactivated_and_reactivated_but_not_all_at_once()
    {
        var a = await Data.MentorAsync();
        var criteria = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();

        (await Rubric.SetActiveAsync(a.UserId, criteria[0].CriterionId, false, default)).ShouldSucceed();
        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Count.ShouldBe(5);
        var all = (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, true, default)).ShouldSucceed();
        all.Count.ShouldBe(6);
        all.Single(c => c.CriterionId == criteria[0].CriterionId).IsActive.ShouldBeFalse();

        (await App.Get<IRubricService>().SetActiveAsync(a.UserId, criteria[0].CriterionId, false, default)).ShouldSucceed();
        foreach (var c in criteria.Skip(1).Take(4))
            (await App.Get<IRubricService>().SetActiveAsync(a.UserId, c.CriterionId, false, default)).ShouldSucceed();
        (await App.Get<IRubricService>().SetActiveAsync(a.UserId, criteria[5].CriterionId, false, default)).ShouldFail(ErrorKind.Conflict, "rubric.last_active_criterion");

        (await App.Get<IRubricService>().SetActiveAsync(a.UserId, criteria[0].CriterionId, true, default)).ShouldSucceed();
        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Reorder_assigns_positions_and_requires_every_criterion_exactly_once()
    {
        var a = await Data.MentorAsync();
        var criteria = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();
        var reversed = criteria.Select(c => c.CriterionId).Reverse().ToList();

        (await Rubric.ReorderAsync(a.UserId, reversed, default)).ShouldSucceed();
        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Select(c => c.CriterionId).ShouldBe(reversed);

        (await App.Get<IRubricService>().ReorderAsync(a.UserId, reversed.Take(5).ToList(), default)).ShouldFail(ErrorKind.Validation, "rubric.invalid_order");
        (await App.Get<IRubricService>().ReorderAsync(a.UserId, [.. reversed.Take(5), reversed[0]], default)).ShouldFail(ErrorKind.Validation, "rubric.invalid_order");
        (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed().Select(c => c.CriterionId).ShouldBe(reversed);
    }

    [Fact]
    public async Task Foreign_mentor_mentee_and_unknown_user_get_not_found_and_nothing_changes()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var criteria = (await Rubric.ListCriteriaAsync(a.UserId, false, default)).ShouldSucceed();
        var ids = criteria.Select(c => c.CriterionId).ToList();
        var target = criteria[0];
        await App.Get<IRubricService>().ListCriteriaAsync(b.UserId, false, default);

        foreach (var actor in new[] { b.UserId, mentee.UserId, Guid.NewGuid() })
        {
            (await App.Get<IRubricService>().UpdateCriterionAsync(actor, target.CriterionId, Valid(), default)).ShouldBeNotFound();
            (await App.Get<IRubricService>().SetActiveAsync(actor, target.CriterionId, false, default)).ShouldBeNotFound();
            (await App.Get<IRubricService>().ReorderAsync(actor, ids, default)).ShouldBeNotFound();
        }
        (await App.Get<IRubricService>().UpdateCriterionAsync(a.UserId, Guid.NewGuid(), Valid(), default)).ShouldBeNotFound();
        (await App.Get<IRubricService>().SetActiveAsync(a.UserId, Guid.NewGuid(), false, default)).ShouldBeNotFound();
        (await App.Get<IRubricService>().ReorderAsync(a.UserId, [.. ids.Take(5), Guid.NewGuid()], default)).ShouldBeNotFound();

        var after = (await App.Get<IRubricService>().ListCriteriaAsync(a.UserId, true, default)).ShouldSucceed();
        after.Select(Describe).ShouldBe(criteria.Select(Describe));
    }

    private static string Describe(RubricCriterionDto c) => $"{c.CriterionId}|{c.Name}|{c.Position}|{c.IsActive}|{string.Join('/', c.LevelDescriptions)}";
}
