using System.Text.Json;
using MentorPath.Application.AiAuthoring;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.AiAuthoring;

public sealed class AiDraftValidatorTests
{
    private static string Json(object o) => JsonSerializer.Serialize(o);

    private static object QuizPayload(object[] options, string scenario = "Scenariusz") => new
    {
        title = "Quiz", scenarioMarkdown = scenario, instructionsMarkdown = "", expectedOutcomeMarkdown = (string?)null, rubricMarkdown = "",
        mentorGuideMarkdown = "Guide", explanationMarkdown = "Wyjaśnienie", discussionQuestions = new[] { "Dlaczego?" }, quizOptions = options,
    };

    [Fact]
    public void Valid_quiz_payload_is_mapped()
    {
        var (content, error) = AiDraftValidator.Parse(ExerciseType.QuizSingleChoice, Json(QuizPayload([
            new { text = "A", isCorrect = true, explanationMarkdown = "tak" },
            new { text = "B", isCorrect = false, explanationMarkdown = "nie" }])), 15);
        error.ShouldBeNull();
        content!.QuizOptions.Count.ShouldBe(2);
        content.DiscussionQuestionsMarkdown.ShouldBe("- Dlaczego?");
        content.EstimatedMinutes.ShouldBe(15);
    }

    [Fact]
    public void Quiz_with_two_correct_answers_is_rejected()
    {
        var (_, error) = AiDraftValidator.Parse(ExerciseType.QuizSingleChoice, Json(QuizPayload([
            new { text = "A", isCorrect = true, explanationMarkdown = "" },
            new { text = "B", isCorrect = true, explanationMarkdown = "" }])), null);
        error.ShouldBe("domain_validation_failed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("{\"foo\":1}")]
    public void Malformed_output_is_rejected(string raw) =>
        AiDraftValidator.Parse(ExerciseType.ArchitecturalDecision, raw, null).Error.ShouldNotBeNull();

    [Fact]
    public void Html_in_output_is_rejected()
    {
        var (_, error) = AiDraftValidator.Parse(ExerciseType.QuizSingleChoice, Json(QuizPayload([
            new { text = "A", isCorrect = true, explanationMarkdown = "" },
            new { text = "B", isCorrect = false, explanationMarkdown = "" }], "<script>alert(1)</script>")), null);
        error.ShouldBe("html_not_allowed");
    }

    [Fact]
    public void Code_fenced_json_is_accepted()
    {
        var payload = Json(new
        {
            title = "AD", scenarioMarkdown = "S", instructionsMarkdown = "I", expectedOutcomeMarkdown = "E", rubricMarkdown = "R",
            mentorGuideMarkdown = "G", explanationMarkdown = (string?)null, discussionQuestions = Array.Empty<string>(), quizOptions = Array.Empty<object>(),
        });
        AiDraftValidator.Parse(ExerciseType.ArchitecturalDecision, $"```json\n{payload}\n```", null).Error.ShouldBeNull();
    }

    [Fact]
    public void Prompt_contains_only_catalog_and_mentor_input()
    {
        var prompt = ExerciseDraftPrompt.BuildUserPrompt("Temat", "Opis", "Cele", ExerciseType.PracticalTask, Difficulty.Advanced, "Cele ćwiczenia", "Kontekst", 30);
        prompt.ShouldContain("Temat");
        prompt.ShouldContain("PracticalTask");
        prompt.ShouldContain("Kontekst");
    }
}
