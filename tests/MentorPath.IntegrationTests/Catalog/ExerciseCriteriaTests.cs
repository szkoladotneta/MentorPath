using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Catalog;

public sealed class ExerciseCriteriaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IExerciseAuthoringService Exercises => App.Get<IExerciseAuthoringService>();
    private IRubricService Rubric => App.Get<IRubricService>();

    private async Task<List<Guid>> CriteriaOfAsync(MentorRef mentor) =>
        (await Rubric.ListCriteriaAsync(mentor.UserId, true, default)).ShouldSucceed().Select(c => c.CriterionId).ToList();

    private async Task<Guid> DraftAsync(MentorRef mentor, ExerciseType type, IReadOnlyList<Guid>? criteria)
    {
        var topic = await Data.TopicAsync(mentor);
        return (await Exercises.CreateDraftAsync(mentor.UserId, new(topic, type, TestDataBuilder.ValidContent(type) with { CriterionIds = criteria }), default)).ShouldSucceed();
    }

    private async Task<IReadOnlyList<Guid>?> SelectionAsync(MentorRef mentor, Guid exerciseId) =>
        (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, exerciseId, default)).ShouldSucceed().Content.CriterionIds;

    [Fact]
    public async Task New_decision_exercise_defaults_to_all_active_criteria_in_rubric_order()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);

        var id = await DraftAsync(a, ExerciseType.ArchitecturalDecision, null);

        (await SelectionAsync(a, id)).ShouldBe(all);
    }

    [Fact]
    public async Task Default_selection_skips_inactive_criteria()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        (await Rubric.SetActiveAsync(a.UserId, all[0], false, default)).ShouldSucceed();

        var id = await DraftAsync(a, ExerciseType.PracticalTask, null);

        (await SelectionAsync(a, id)).ShouldBe(all.Skip(1));
    }

    [Fact]
    public async Task Quiz_has_no_criteria_and_rejects_a_selection()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        var topic = await Data.TopicAsync(a);

        var id = await DraftAsync(a, ExerciseType.QuizSingleChoice, null);
        (await SelectionAsync(a, id)).ShouldBeEmpty();
        (await Exercises.CreateDraftAsync(a.UserId, new(topic, ExerciseType.QuizSingleChoice,
            TestDataBuilder.ValidContent(ExerciseType.QuizSingleChoice) with { CriterionIds = [all[0]] }), default))
            .ShouldFail(ErrorKind.Validation, "catalog.invalid_exercise").FieldErrors!.ShouldContainKey("CriterionIds");
        (await App.Get<IExerciseAuthoringService>().PublishAsync(a.UserId, id, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Explicit_selection_is_stored_and_replaced_on_update_while_null_keeps_it()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        var id = await DraftAsync(a, ExerciseType.ArchitecturalDecision, [all[2], all[0]]);
        (await SelectionAsync(a, id)).ShouldBe([all[0], all[2]]);

        var content = TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision);
        (await Exercises.UpdateDraftAsync(a.UserId, id, content with { Title = "Zmieniony" }, default)).ShouldSucceed();
        (await SelectionAsync(a, id)).ShouldBe([all[0], all[2]]);

        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(a.UserId, id, content with { CriterionIds = [all[5], all[1]] }, default)).ShouldSucceed();
        (await SelectionAsync(a, id)).ShouldBe([all[1], all[5]]);
    }

    [Fact]
    public async Task Selection_must_contain_unique_active_criteria_of_the_workspace()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        var foreign = await CriteriaOfAsync(b);
        var topic = await Data.TopicAsync(a);
        (await Rubric.SetActiveAsync(a.UserId, all[3], false, default)).ShouldSucceed();
        var content = TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision);

        (await Exercises.CreateDraftAsync(a.UserId, new(topic, ExerciseType.ArchitecturalDecision, content with { CriterionIds = [all[0], foreign[0]] }), default)).ShouldBeNotFound();
        (await App.Get<IExerciseAuthoringService>().CreateDraftAsync(a.UserId, new(topic, ExerciseType.ArchitecturalDecision, content with { CriterionIds = [all[3]] }), default))
            .ShouldFail(ErrorKind.Validation, "catalog.invalid_exercise");
        (await App.Get<IExerciseAuthoringService>().CreateDraftAsync(a.UserId, new(topic, ExerciseType.ArchitecturalDecision, content with { CriterionIds = [all[0], all[0]] }), default))
            .ShouldFail(ErrorKind.Validation, "catalog.invalid_exercise");

        var id = await DraftAsync(a, ExerciseType.ArchitecturalDecision, null);
        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(a.UserId, id, content with { CriterionIds = [foreign[1]] }, default)).ShouldBeNotFound();
        (await SelectionAsync(a, id))!.ShouldNotContain(foreign[1]);
        (await App.Db(ctx => ctx.ExerciseCriteria.CountAsync(x => x.CriterionId == foreign[0] || x.CriterionId == foreign[1]))).ShouldBe(0);
    }

    [Fact]
    public async Task Publish_requires_at_least_one_active_selected_criterion()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        var id = await DraftAsync(a, ExerciseType.ArchitecturalDecision, []);

        (await Exercises.PublishAsync(a.UserId, id, default)).ShouldFail(ErrorKind.Validation, "catalog.publish_invalid")
            .FieldErrors!.ShouldContainKey("CriterionIds");

        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(a.UserId, id,
            TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision) with { CriterionIds = [all[0]] }, default)).ShouldSucceed();
        (await Rubric.SetActiveAsync(a.UserId, all[0], false, default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().PublishAsync(a.UserId, id, default)).ShouldFail(ErrorKind.Validation, "catalog.publish_invalid");

        (await Rubric.SetActiveAsync(a.UserId, all[0], true, default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().PublishAsync(a.UserId, id, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Clone_copies_the_selection_and_delete_removes_it()
    {
        var a = await Data.MentorAsync();
        var all = await CriteriaOfAsync(a);
        var id = await DraftAsync(a, ExerciseType.ArchitecturalDecision, [all[1], all[4]]);

        var clone = (await Exercises.CloneAsDraftAsync(a.UserId, id, default)).ShouldSucceed();
        (await SelectionAsync(a, clone)).ShouldBe([all[1], all[4]]);

        (await App.Get<IExerciseAuthoringService>().DeleteDraftAsync(a.UserId, id, default)).ShouldSucceed();
        (await App.Db(ctx => ctx.ExerciseCriteria.CountAsync(x => x.ExerciseId == id))).ShouldBe(0);
        (await SelectionAsync(a, clone)).ShouldBe([all[1], all[4]]);
    }

    [Fact]
    public async Task Selection_is_not_exposed_to_foreign_mentor_or_mentee_dto()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);

        (await Exercises.GetMentorExerciseAsync(b.UserId, asg.ExerciseId, default)).ShouldBeNotFound();
        var menteeView = (await App.Get<IExerciseAuthoringService>().GetMenteeExerciseAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        menteeView.GetType().GetProperties().Select(p => p.Name).ShouldNotContain("CriterionIds");
    }
}
