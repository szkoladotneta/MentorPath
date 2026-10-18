using System.Text.RegularExpressions;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Catalog;

/// <summary>Domain rules for exercise content (D-13).</summary>
public static partial class ExerciseValidator
{
    public static FieldErrors ValidateDraft(ExerciseType type, ExerciseContent c)
    {
        var e = new FieldErrors();
        if (!Enum.IsDefined(type)) e.Add("Type", "Nieznany typ ćwiczenia.");
        e.Required(nameof(c.Title), c.Title, InputLimits.Title, "Tytuł");
        e.Optional(nameof(c.ScenarioMarkdown), c.ScenarioMarkdown, InputLimits.Markdown, "Scenariusz");
        e.Optional(nameof(c.InstructionsMarkdown), c.InstructionsMarkdown, InputLimits.Markdown, "Wymagania");
        e.Optional(nameof(c.ExpectedOutcomeMarkdown), c.ExpectedOutcomeMarkdown, InputLimits.Markdown, "Oczekiwany rezultat");
        e.Optional(nameof(c.RubricMarkdown), c.RubricMarkdown, InputLimits.Markdown, "Kryteria oceny");
        e.Optional(nameof(c.DiscussionQuestionsMarkdown), c.DiscussionQuestionsMarkdown, InputLimits.Markdown, "Pytania");
        e.Optional(nameof(c.ExplanationMarkdown), c.ExplanationMarkdown, InputLimits.Markdown, "Wyjaśnienie");
        e.Optional(nameof(c.MentorGuideMarkdown), c.MentorGuideMarkdown, InputLimits.Markdown, "Mentor guide");
        if (c.EstimatedMinutes is not null) e.Range(nameof(c.EstimatedMinutes), c.EstimatedMinutes, 1, 600, "Szacowany czas");

        var options = c.QuizOptions ?? [];
        if (type != ExerciseType.QuizSingleChoice && options.Count > 0)
            e.Add(nameof(c.QuizOptions), "Opcje odpowiedzi dotyczą tylko quizu.");
        if (options.Count > InputLimits.QuizOptionsMax)
            e.Add(nameof(c.QuizOptions), $"Quiz może mieć maksymalnie {InputLimits.QuizOptionsMax} opcji.");
        if (options.Count(o => o.IsCorrect) > 1)
            e.Add(nameof(c.QuizOptions), "Quiz może mieć dokładnie jedną poprawną odpowiedź.");
        for (var i = 0; i < options.Count; i++)
        {
            e.Optional($"QuizOptions[{i}].Text", options[i].Text, InputLimits.ShortText, "Treść opcji");
            e.Optional($"QuizOptions[{i}].ExplanationMarkdown", options[i].ExplanationMarkdown, InputLimits.Markdown, "Wyjaśnienie opcji");
        }
        return e;
    }

    public static FieldErrors ValidateForPublish(ExerciseType type, ExerciseContent c)
    {
        var e = ValidateDraft(type, c);
        e.Required(nameof(c.ScenarioMarkdown), c.ScenarioMarkdown, InputLimits.Markdown, type == ExerciseType.PracticalTask ? "Opis zadania" : "Scenariusz");
        var options = c.QuizOptions ?? [];

        switch (type)
        {
            case ExerciseType.QuizSingleChoice:
                e.Required(nameof(c.ExplanationMarkdown), c.ExplanationMarkdown, InputLimits.Markdown, "Wyjaśnienie odpowiedzi");
                if (!string.IsNullOrWhiteSpace(c.ExpectedOutcomeMarkdown))
                    e.Add(nameof(c.ExpectedOutcomeMarkdown), "Quiz nie ma oczekiwanego rezultatu.");
                if (options.Count < InputLimits.QuizOptionsMin)
                    e.Add(nameof(c.QuizOptions), $"Quiz wymaga co najmniej {InputLimits.QuizOptionsMin} opcji.");
                if (options.Count(o => o.IsCorrect) != 1)
                    e.Add(nameof(c.QuizOptions), "Quiz musi mieć dokładnie jedną poprawną odpowiedź.");
                for (var i = 0; i < options.Count; i++)
                    e.Required($"QuizOptions[{i}].Text", options[i].Text, InputLimits.ShortText, "Treść opcji");
                break;
            case ExerciseType.ArchitecturalDecision:
                e.Required(nameof(c.InstructionsMarkdown), c.InstructionsMarkdown, InputLimits.Markdown, "Ograniczenia i wymagania");
                e.Required(nameof(c.ExpectedOutcomeMarkdown), c.ExpectedOutcomeMarkdown, InputLimits.Markdown, "Oczekiwane rezultaty");
                e.Required(nameof(c.RubricMarkdown), c.RubricMarkdown, InputLimits.Markdown, "Kryteria oceny");
                if (!string.IsNullOrWhiteSpace(c.ExplanationMarkdown))
                    e.Add(nameof(c.ExplanationMarkdown), "Wyjaśnienie dotyczy tylko quizu.");
                break;
            case ExerciseType.PracticalTask:
                e.Required(nameof(c.InstructionsMarkdown), c.InstructionsMarkdown, InputLimits.Markdown, "Wymagania");
                e.Required(nameof(c.ExpectedOutcomeMarkdown), c.ExpectedOutcomeMarkdown, InputLimits.Markdown, "Oczekiwane artefakty");
                e.Required(nameof(c.RubricMarkdown), c.RubricMarkdown, InputLimits.Markdown, "Kryteria oceny");
                e.Required(nameof(c.DiscussionQuestionsMarkdown), c.DiscussionQuestionsMarkdown, InputLimits.Markdown, "Pytania o decyzje implementacyjne");
                if (!string.IsNullOrWhiteSpace(c.ExplanationMarkdown))
                    e.Add(nameof(c.ExplanationMarkdown), "Wyjaśnienie dotyczy tylko quizu.");
                break;
        }
        return e;
    }

    public static bool ContainsHtml(ExerciseContent c) =>
        new[] { c.Title, c.ScenarioMarkdown, c.InstructionsMarkdown, c.ExpectedOutcomeMarkdown, c.RubricMarkdown,
                c.DiscussionQuestionsMarkdown, c.ExplanationMarkdown, c.MentorGuideMarkdown }
            .Concat((c.QuizOptions ?? []).SelectMany(o => new[] { o.Text, o.ExplanationMarkdown }))
            .Any(FieldErrors.ContainsHtml);

    public static bool IsValidSlug(string? slug) => slug is not null && SlugRegex().IsMatch(slug);

    public static string Slugify(string title)
    {
        var map = new Dictionary<char, string> { ['ą'] = "a", ['ć'] = "c", ['ę'] = "e", ['ł'] = "l", ['ń'] = "n", ['ó'] = "o", ['ś'] = "s", ['ź'] = "z", ['ż'] = "z" };
        var chars = title.ToLowerInvariant().Select(ch => map.TryGetValue(ch, out var r) ? r : char.IsLetterOrDigit(ch) && ch < 128 ? ch.ToString() : "-");
        var slug = MultiDash().Replace(string.Concat(chars), "-").Trim('-');
        return slug.Length > InputLimits.Slug ? slug[..InputLimits.Slug].Trim('-') : slug;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultiDash();
}
