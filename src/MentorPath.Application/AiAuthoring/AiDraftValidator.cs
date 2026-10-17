using System.Text.Json;
using System.Text.Json.Serialization;
using MentorPath.Application.Catalog;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.AiAuthoring;

/// <summary>Treats provider output as untrusted input: schema, domain rules and HTML are validated before a draft is saved.</summary>
public static class AiDraftValidator
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        MaxDepth = 8,
    };

    public static (ExerciseContent? Content, string? Error) Parse(ExerciseType type, string? rawJson, int? estimatedMinutes)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return (null, "empty_output");
        if (rawJson.Length > 200_000) return (null, "output_too_large");

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(StripCodeFence(rawJson), Json);
        }
        catch (JsonException)
        {
            return (null, "invalid_json");
        }
        if (payload is null || payload.Title is null || payload.ScenarioMarkdown is null) return (null, "schema_mismatch");

        var isQuiz = type == ExerciseType.QuizSingleChoice;
        var questions = (payload.DiscussionQuestions ?? []).Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        var content = new ExerciseContent(
            payload.Title,
            payload.ScenarioMarkdown,
            payload.InstructionsMarkdown,
            isQuiz ? null : payload.ExpectedOutcomeMarkdown,
            payload.RubricMarkdown,
            questions.Count > 0 ? string.Join('\n', questions.Select(q => $"- {q.Trim()}")) : null,
            isQuiz ? payload.ExplanationMarkdown : null,
            payload.MentorGuideMarkdown,
            estimatedMinutes,
            isQuiz ? (payload.QuizOptions ?? []).Select(o => new QuizOptionInput(o.Text ?? "", o.IsCorrect, o.ExplanationMarkdown)).ToList() : []);

        if (ExerciseValidator.ContainsHtml(content)) return (null, "html_not_allowed");
        var errors = ExerciseValidator.ValidateForPublish(type, content);
        return errors.HasErrors ? (null, "domain_validation_failed") : (content, null);
    }

    private static string StripCodeFence(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("```", StringComparison.Ordinal)) return s;
        var firstNewLine = s.IndexOf('\n');
        var lastFence = s.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine > 0 && lastFence > firstNewLine ? s[(firstNewLine + 1)..lastFence] : s;
    }

    private sealed class Payload
    {
        public string? Title { get; set; }
        public string? ScenarioMarkdown { get; set; }
        public string? InstructionsMarkdown { get; set; }
        public string? ExpectedOutcomeMarkdown { get; set; }
        public string? RubricMarkdown { get; set; }
        public string? MentorGuideMarkdown { get; set; }
        public string? ExplanationMarkdown { get; set; }
        public List<string>? DiscussionQuestions { get; set; }
        public List<OptionPayload>? QuizOptions { get; set; }
    }

    private sealed class OptionPayload
    {
        public string? Text { get; set; }
        public bool IsCorrect { get; set; }
        public string? ExplanationMarkdown { get; set; }
    }
}
