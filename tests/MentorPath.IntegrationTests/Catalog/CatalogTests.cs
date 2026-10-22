using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Catalog;

public sealed class CatalogTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IExerciseAuthoringService Exercises => App.Get<IExerciseAuthoringService>();
    private ITopicService Topics => App.Get<ITopicService>();

    [Fact]
    public async Task Slug_is_unique_per_workspace_but_reusable_across_workspaces()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var request = new UpsertTopicRequest("CQRS", "cqrs", "Architektura", Difficulty.Advanced, "Opis", "Cele");
        (await Topics.CreateAsync(a.UserId, request, default)).ShouldSucceed();
        (await App.Get<ITopicService>().CreateAsync(a.UserId, request, default)).ShouldFail(ErrorKind.Validation, "catalog.slug_taken");
        (await App.Get<ITopicService>().CreateAsync(b.UserId, request, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Topic_rationale_round_trips_and_blank_is_normalized()
    {
        var mentor = await Data.MentorAsync();
        var existingTopicId = await Data.TopicAsync(mentor);
        (await Topics.GetAsync(mentor.UserId, existingTopicId, default)).ShouldSucceed().WhyItMattersMarkdown.ShouldBeNull();

        var blankTopicId = (await Topics.CreateAsync(mentor.UserId,
            new("Temat z pustym rationale", "temat-z-pustym-rationale", "Architektura", Difficulty.Beginner, "Opis", "Cele",
                WhyItMattersMarkdown: " \t "), default)).ShouldSucceed();
        (await Topics.GetAsync(mentor.UserId, blankTopicId, default)).ShouldSucceed().WhyItMattersMarkdown.ShouldBeNull();

        var topicId = (await Topics.CreateAsync(mentor.UserId,
            new("Temat z rationale", "temat-z-rationale", "Architektura", Difficulty.Advanced, "Opis ogólny", "- Cele",
                WhyItMattersMarkdown: "## Dlaczego\n\nPrzygotowuje do decyzji."), default)).ShouldSucceed();

        var created = (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        created.DescriptionMarkdown.ShouldBe("Opis ogólny");
        created.LearningObjectivesMarkdown.ShouldBe("- Cele");
        created.WhyItMattersMarkdown.ShouldBe("## Dlaczego\n\nPrzygotowuje do decyzji.");

        (await Topics.UpdateAsync(mentor.UserId, topicId,
            new(created.Title, created.Slug, created.Category, created.Difficulty, created.DescriptionMarkdown,
                created.LearningObjectivesMarkdown, WhyItMattersMarkdown: "Zaktualizowane uzasadnienie."), default)).ShouldSucceed();
        var updated = (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        updated.DescriptionMarkdown.ShouldBe("Opis ogólny");
        updated.LearningObjectivesMarkdown.ShouldBe("- Cele");
        updated.WhyItMattersMarkdown.ShouldBe("Zaktualizowane uzasadnienie.");

        (await Topics.UpdateAsync(mentor.UserId, topicId,
            new(updated.Title, updated.Slug, updated.Category, updated.Difficulty, updated.DescriptionMarkdown,
                updated.LearningObjectivesMarkdown, WhyItMattersMarkdown: "  \n\t "), default)).ShouldSucceed();
        (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().WhyItMattersMarkdown.ShouldBeNull();
    }

    [Fact]
    public async Task Topic_rationale_create_accepts_the_limit_and_rejects_oversized_values()
    {
        var mentor = await Data.MentorAsync();
        var maximum = new string('r', InputLimits.Markdown);
        var valid = new UpsertTopicRequest("Temat graniczny", "temat-graniczny", "Architektura", Difficulty.Beginner,
            "Opis", "Cele", WhyItMattersMarkdown: maximum);

        var topicId = (await Topics.CreateAsync(mentor.UserId, valid, default)).ShouldSucceed();
        (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().WhyItMattersMarkdown.ShouldBe(maximum);

        var excessive = valid with { Title = "Temat za długi", Slug = "temat-za-dlugi", WhyItMattersMarkdown = maximum + "r" };
        var error = (await Topics.CreateAsync(mentor.UserId, excessive, default))
            .ShouldFail(ErrorKind.Validation, "catalog.invalid_topic");
        error.FieldErrors!.ShouldContainKey(nameof(UpsertTopicRequest.WhyItMattersMarkdown));
        (await Topics.ListAsync(mentor.UserId, true, default)).ShouldSucceed().ShouldNotContain(t => t.Slug == excessive.Slug);
    }

    [Fact]
    public async Task Topic_rationale_accepts_markdown_limit_and_rejects_excess_without_overwriting()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);
        var topic = (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        var maximum = new string('x', InputLimits.Markdown);

        (await Topics.UpdateAsync(mentor.UserId, topicId,
            new(topic.Title, topic.Slug, topic.Category, topic.Difficulty, topic.DescriptionMarkdown,
                topic.LearningObjectivesMarkdown, WhyItMattersMarkdown: maximum), default)).ShouldSucceed();

        var excessive = maximum + "x";
        var error = (await Topics.UpdateAsync(mentor.UserId, topicId,
            new(topic.Title, topic.Slug, topic.Category, topic.Difficulty, topic.DescriptionMarkdown,
                topic.LearningObjectivesMarkdown, WhyItMattersMarkdown: excessive), default))
            .ShouldFail(ErrorKind.Validation, "catalog.invalid_topic");
        error.FieldErrors!.ShouldContainKey(nameof(UpsertTopicRequest.WhyItMattersMarkdown));
        (await Topics.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().WhyItMattersMarkdown.ShouldBe(maximum);
    }

    [Theory]
    [InlineData(ExerciseType.QuizSingleChoice)]
    [InlineData(ExerciseType.ArchitecturalDecision)]
    [InlineData(ExerciseType.PracticalTask)]
    public async Task Mentor_can_create_and_publish_each_exercise_type(ExerciseType type)
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var id = await Data.PublishedExerciseAsync(mentor, topic, type);
        var dto = (await Exercises.GetMentorExerciseAsync(mentor.UserId, id, default)).ShouldSucceed();
        dto.Status.ShouldBe(ExerciseStatus.Published);
        dto.Source.ShouldBe(ExerciseSource.Manual);
        dto.PublishedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Incomplete_quiz_cannot_be_published()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var noCorrect = TestDataBuilder.ValidContent(ExerciseType.QuizSingleChoice) with
        {
            QuizOptions = [new("A", false, null), new("B", false, null)],
        };
        var id = (await Exercises.CreateDraftAsync(mentor.UserId, new(topic, ExerciseType.QuizSingleChoice, noCorrect), default)).ShouldSucceed();
        var error = (await App.Get<IExerciseAuthoringService>().PublishAsync(mentor.UserId, id, default)).ShouldFail(ErrorKind.Validation, "catalog.publish_invalid");
        error.FieldErrors!.ShouldContainKey("QuizOptions");

        var oneOption = noCorrect with { QuizOptions = [new("A", true, null)] };
        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(mentor.UserId, id, oneOption, default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().PublishAsync(mentor.UserId, id, default)).ShouldFail(ErrorKind.Validation);
    }

    [Fact]
    public async Task Draft_with_two_correct_options_is_rejected_on_save()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var twoCorrect = TestDataBuilder.ValidContent(ExerciseType.QuizSingleChoice) with { QuizOptions = [new("A", true, null), new("B", true, null)] };
        (await Exercises.CreateDraftAsync(mentor.UserId, new(topic, ExerciseType.QuizSingleChoice, twoCorrect), default)).ShouldFail(ErrorKind.Validation);
    }

    [Fact]
    public async Task Published_exercise_is_immutable_and_can_be_cloned_to_draft()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var id = await Data.PublishedExerciseAsync(mentor, topic, ExerciseType.ArchitecturalDecision);

        (await Exercises.UpdateDraftAsync(mentor.UserId, id, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision) with { Title = "Zmiana" }, default))
            .ShouldFail(ErrorKind.Conflict, "catalog.exercise_not_draft");
        (await App.Get<IExerciseAuthoringService>().DeleteDraftAsync(mentor.UserId, id, default)).ShouldFail(ErrorKind.Conflict);

        var cloneId = (await App.Get<IExerciseAuthoringService>().CloneAsDraftAsync(mentor.UserId, id, default)).ShouldSucceed();
        var clone = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, cloneId, default)).ShouldSucceed();
        clone.Status.ShouldBe(ExerciseStatus.Draft);
        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(mentor.UserId, cloneId, clone.Content with { Title = "Nowa wersja" }, default)).ShouldSucceed();

        var original = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, id, default)).ShouldSucceed();
        original.Content.Title.ShouldBe("Decyzja");
    }

    [Fact]
    public async Task Quiz_options_can_be_replaced_while_draft()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var content = TestDataBuilder.ValidContent(ExerciseType.QuizSingleChoice);
        var id = (await Exercises.CreateDraftAsync(mentor.UserId, new(topic, ExerciseType.QuizSingleChoice, content), default)).ShouldSucceed();
        var updated = content with { QuizOptions = [new("X", false, null), new("Y", true, null)] };
        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(mentor.UserId, id, updated, default)).ShouldSucceed();
        var dto = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, id, default)).ShouldSucceed();
        dto.Content.QuizOptions.Select(o => o.Text).ShouldBe(["X", "Y"]);
        dto.Content.QuizOptions.Single(o => o.IsCorrect).Text.ShouldBe("Y");
    }

    [Fact]
    public async Task Archived_topic_blocks_new_exercises()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        (await Topics.SetArchivedAsync(mentor.UserId, topic, true, default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().CreateDraftAsync(mentor.UserId,
            new(topic, ExerciseType.ArchitecturalDecision, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision)), default))
            .ShouldFail(ErrorKind.Validation, "catalog.topic_archived");
        (await App.Get<ITopicService>().ListAsync(mentor.UserId, false, default)).ShouldSucceed().ShouldNotContain(t => t.TopicId == topic);
    }

    [Fact]
    public async Task Catalog_is_isolated_between_workspaces()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var sameWorkspaceMentee = await Data.MenteeAsync(a);
        var foreignMentee = await Data.MenteeAsync(b);
        var topic = await Data.TopicAsync(a);
        var draftId = (await Exercises.CreateDraftAsync(a.UserId, new(topic, ExerciseType.ArchitecturalDecision, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision)), default)).ShouldSucceed();
        var published = await Data.PublishedExerciseAsync(a, topic, ExerciseType.QuizSingleChoice);
        var svc = App.Get<IExerciseAuthoringService>();
        var topics = App.Get<ITopicService>();
        var unknown = Guid.NewGuid();
        var invalidUpdate = new UpsertTopicRequest("x", "x", "x", Difficulty.Beginner, "x", "x",
            WhyItMattersMarkdown: "Private rationale");

        (await topics.ListAsync(b.UserId, true, default)).ShouldSucceed().ShouldBeEmpty();
        (await topics.GetAsync(b.UserId, topic, default)).ShouldBeNotFound();
        (await topics.GetAsync(sameWorkspaceMentee.UserId, topic, default)).ShouldBeNotFound();
        (await topics.GetAsync(foreignMentee.UserId, topic, default)).ShouldBeNotFound();
        (await topics.GetAsync(unknown, topic, default)).ShouldBeNotFound();
        (await topics.UpdateAsync(b.UserId, topic, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.UpdateAsync(sameWorkspaceMentee.UserId, topic, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.UpdateAsync(foreignMentee.UserId, topic, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.UpdateAsync(unknown, topic, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.UpdateAsync(a.UserId, Guid.NewGuid(), invalidUpdate, default)).ShouldBeNotFound();
        (await topics.CreateAsync(sameWorkspaceMentee.UserId, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.CreateAsync(foreignMentee.UserId, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.CreateAsync(unknown, invalidUpdate, default)).ShouldBeNotFound();
        (await topics.SetArchivedAsync(b.UserId, topic, true, default)).ShouldBeNotFound();
        (await svc.GetMentorExerciseAsync(b.UserId, draftId, default)).ShouldBeNotFound();
        (await svc.UpdateDraftAsync(b.UserId, draftId, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision), default)).ShouldBeNotFound();
        (await svc.PublishAsync(b.UserId, draftId, default)).ShouldBeNotFound();
        (await svc.DeleteDraftAsync(b.UserId, draftId, default)).ShouldBeNotFound();
        (await svc.CloneAsDraftAsync(b.UserId, published, default)).ShouldBeNotFound();
        (await svc.ArchiveAsync(b.UserId, published, default)).ShouldBeNotFound();
        (await svc.ListAsync(b.UserId, new(null, null, null), default)).ShouldSucceed().ShouldBeEmpty();
        (await svc.CreateDraftAsync(b.UserId, new(topic, ExerciseType.ArchitecturalDecision, TestDataBuilder.ValidContent(ExerciseType.ArchitecturalDecision)), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Mentee_sees_exercise_only_through_published_assignment_without_private_fields()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var outsider = await Data.MenteeAsync(await Data.MentorAsync());
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);

        var dto = (await Exercises.GetMenteeExerciseAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        dto.QuizOptions.Count.ShouldBe(3);
        (await App.Get<IExerciseAuthoringService>().GetMenteeExerciseAsync(outsider.UserId, asg.AssignmentId, default)).ShouldBeNotFound();
        (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentee.UserId, asg.ExerciseId, default)).ShouldBeNotFound();
    }
}
