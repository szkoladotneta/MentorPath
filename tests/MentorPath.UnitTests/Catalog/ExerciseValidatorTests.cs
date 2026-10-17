using MentorPath.Application.Catalog;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.Catalog;

public sealed class ExerciseValidatorTests
{
    private static ExerciseContent Quiz(params QuizOptionInput[] options) =>
        new ExerciseContent("Quiz", "Scenariusz", null, null, null, null, "Wyjaśnienie", null, 10, options);

    [Fact]
    public void Valid_quiz_passes_publish_validation() =>
        ExerciseValidator.ValidateForPublish(ExerciseType.QuizSingleChoice, Quiz(new QuizOptionInput("A", true, null), new QuizOptionInput("B", false, null))).HasErrors.ShouldBeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Quiz_must_have_exactly_one_correct_option(int correct)
    {
        var options = Enumerable.Range(0, 3).Select(i => new QuizOptionInput($"O{i}", i < correct, null)).ToArray();
        var errors = ExerciseValidator.ValidateForPublish(ExerciseType.QuizSingleChoice, Quiz(options));
        errors.ToDictionary().ShouldContainKey("QuizOptions");
    }

    [Fact]
    public void Quiz_needs_at_least_two_and_at_most_six_options()
    {
        ExerciseValidator.ValidateForPublish(ExerciseType.QuizSingleChoice, Quiz(new QuizOptionInput("A", true, null))).ToDictionary().ShouldContainKey("QuizOptions");
        var seven = Enumerable.Range(0, 7).Select(i => new QuizOptionInput($"O{i}", i == 0, null)).ToArray();
        ExerciseValidator.ValidateDraft(ExerciseType.QuizSingleChoice, Quiz(seven)).ToDictionary().ShouldContainKey("QuizOptions");
    }

    [Fact]
    public void Quiz_requires_explanation_and_option_texts()
    {
        var content = Quiz(new QuizOptionInput("", true, null), new QuizOptionInput("B", false, null)) with { ExplanationMarkdown = null };
        var errors = ExerciseValidator.ValidateForPublish(ExerciseType.QuizSingleChoice, content).ToDictionary();
        errors.ShouldContainKey("ExplanationMarkdown");
        errors.ShouldContainKey("QuizOptions[0].Text");
    }

    [Fact]
    public void Architectural_decision_requires_constraints_outcomes_and_rubric()
    {
        var content = new ExerciseContent("AD", "Scenariusz", null, null, null, null, null, null, null, []);
        var errors = ExerciseValidator.ValidateForPublish(ExerciseType.ArchitecturalDecision, content).ToDictionary();
        errors.Keys.ShouldBe(["InstructionsMarkdown", "ExpectedOutcomeMarkdown", "RubricMarkdown"], ignoreOrder: true);
    }

    [Fact]
    public void Practical_task_requires_implementation_questions()
    {
        var content = new ExerciseContent("PT", "Opis", "Wymagania", "Artefakty", "Kryteria", null, null, null, null, []);
        ExerciseValidator.ValidateForPublish(ExerciseType.PracticalTask, content).ToDictionary().Keys.ShouldBe(["DiscussionQuestionsMarkdown"]);
    }

    [Fact]
    public void Non_quiz_cannot_have_options()
    {
        var content = new ExerciseContent("AD", "S", "I", "E", "R", null, null, null, null, [new QuizOptionInput("A", true, null)]);
        ExerciseValidator.ValidateDraft(ExerciseType.ArchitecturalDecision, content).ToDictionary().ShouldContainKey("QuizOptions");
    }

    [Fact]
    public void Draft_allows_incomplete_content_but_enforces_lengths()
    {
        var content = new ExerciseContent("Tytuł", "", null, null, null, null, null, null, null, []);
        ExerciseValidator.ValidateDraft(ExerciseType.ArchitecturalDecision, content).HasErrors.ShouldBeFalse();
        ExerciseValidator.ValidateDraft(ExerciseType.ArchitecturalDecision, content with { Title = new string('x', 201) }).HasErrors.ShouldBeTrue();
        ExerciseValidator.ValidateDraft(ExerciseType.ArchitecturalDecision, content with { EstimatedMinutes = 0 }).HasErrors.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Modularny monolit", "modularny-monolit")]
    [InlineData("Spójność  danych / transakcje!", "spojnosc-danych-transakcje")]
    [InlineData("Łączenie żądań", "laczenie-zadan")]
    public void Slugify_produces_valid_slugs(string title, string expected)
    {
        var slug = ExerciseValidator.Slugify(title);
        slug.ShouldBe(expected);
        ExerciseValidator.IsValidSlug(slug).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Ok-Slug", false)]
    [InlineData("ok slug", false)]
    [InlineData("-ok", false)]
    [InlineData("ok-slug-1", true)]
    public void Slug_format(string slug, bool valid) => ExerciseValidator.IsValidSlug(slug).ShouldBe(valid);
}
