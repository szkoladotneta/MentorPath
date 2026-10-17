using System.Text;
using MentorPath.Data.Enums;

namespace MentorPath.Application.AiAuthoring;

internal static class ExerciseDraftPrompt
{
    public const string Version = "exercise-draft-v1";

    public const string SystemPrompt = """
        Jesteś doświadczonym architektem .NET i mentorem. Tworzysz SZKIC ćwiczenia dla mentora prowadzącego mentoring architektury backendowej.
        Zasady:
        - Twórz realistyczne scenariusze biznesowe i techniczne osadzone w ekosystemie .NET (ASP.NET Core, EF Core, PostgreSQL, kolejki, chmura).
        - Wymuszaj analizę kompromisów (trade-offs), ryzyk i alternatyw. Unikaj pytań czysto definicyjnych.
        - Nie zakładaj, że zadanie architektoniczne ma jedną poprawną odpowiedź (poza quizem jednokrotnego wyboru).
        - Wyraźnie rozdzielaj treść dla mentee od prywatnego mentorGuideMarkdown (wskazówki oceny, typowe błędy, pytania pomocnicze).
        - Pisz po polsku, w Markdown. Nie używaj HTML.
        - Zwróć WYŁĄCZNIE obiekt JSON zgodny ze schematem.
        Pola wg typu:
        - QuizSingleChoice: scenarioMarkdown (scenariusz + pytanie), 3–5 quizOptions z DOKŁADNIE jedną isCorrect=true, każda z explanationMarkdown, explanationMarkdown (wyjaśnienie poprawnej odpowiedzi), expectedOutcomeMarkdown = null.
        - ArchitecturalDecision: scenarioMarkdown, instructionsMarkdown (ograniczenia i wymagania), expectedOutcomeMarkdown (oczekiwane rezultaty decyzji), rubricMarkdown (kryteria oceny), quizOptions = [], explanationMarkdown = null.
        - PracticalTask: scenarioMarkdown (opis zadania), instructionsMarkdown (wymagania), expectedOutcomeMarkdown (oczekiwane artefakty, np. repozytorium, ADR, diagram), rubricMarkdown, discussionQuestions (pytania o decyzje implementacyjne), quizOptions = [], explanationMarkdown = null.
        """;

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title","scenarioMarkdown","instructionsMarkdown","expectedOutcomeMarkdown","rubricMarkdown","mentorGuideMarkdown","explanationMarkdown","discussionQuestions","quizOptions"],
          "properties": {
            "title": { "type": "string" },
            "scenarioMarkdown": { "type": "string" },
            "instructionsMarkdown": { "type": "string" },
            "expectedOutcomeMarkdown": { "type": ["string","null"] },
            "rubricMarkdown": { "type": "string" },
            "mentorGuideMarkdown": { "type": "string" },
            "explanationMarkdown": { "type": ["string","null"] },
            "discussionQuestions": { "type": "array", "items": { "type": "string" } },
            "quizOptions": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["text","isCorrect","explanationMarkdown"],
                "properties": {
                  "text": { "type": "string" },
                  "isCorrect": { "type": "boolean" },
                  "explanationMarkdown": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    /// <summary>Builds the user prompt from catalog data and mentor input only — never mentee data (AI-05).</summary>
    public static string BuildUserPrompt(string topicTitle, string topicDescription, string topicObjectives,
        ExerciseType type, Difficulty difficulty, string learningObjectives, string? context, int? estimatedMinutes)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Typ ćwiczenia: {type}");
        sb.AppendLine($"Poziom trudności: {difficulty}");
        if (estimatedMinutes is not null) sb.AppendLine($"Szacowany czas wykonania: {estimatedMinutes} min");
        sb.AppendLine();
        sb.AppendLine($"Temat: {topicTitle}");
        sb.AppendLine($"Opis tematu:\n{topicDescription}");
        sb.AppendLine($"Cele tematu:\n{topicObjectives}");
        sb.AppendLine();
        sb.AppendLine($"Cele edukacyjne ćwiczenia:\n{learningObjectives}");
        if (!string.IsNullOrWhiteSpace(context)) sb.AppendLine($"\nKontekst i ograniczenia od mentora:\n{context}");
        return sb.ToString();
    }
}
