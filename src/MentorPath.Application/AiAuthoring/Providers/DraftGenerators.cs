using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MentorPath.Application.AiAuthoring.Contracts;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.AiAuthoring.Providers;

/// <summary>Deterministic provider for development, CI and tests (D-19).</summary>
internal sealed class FakeExerciseDraftGenerator(IOptions<AiOptions> options) : IExerciseDraftGenerator
{
    public string ProviderName => "Fake";

    public async Task<DraftGenerationResponse> GenerateAsync(DraftGenerationPrompt prompt, CancellationToken ct)
    {
        switch (options.Value.FakeMode)
        {
            case "Error":
                return new DraftGenerationResponse(false, null, "fake-model", null, null, "provider_error");
            case "Timeout":
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                break;
            case "InvalidJson":
                return new DraftGenerationResponse(true, "{ not json", "fake-model", 10, 5, null);
        }

        var type = prompt.UserPrompt.Contains("QuizSingleChoice", StringComparison.Ordinal) ? "quiz"
            : prompt.UserPrompt.Contains("PracticalTask", StringComparison.Ordinal) ? "practical" : "decision";
        var topic = prompt.UserPrompt.Split('\n').FirstOrDefault(l => l.StartsWith("Temat: ", StringComparison.Ordinal))?[7..].Trim() ?? "Architektura";

        object payload = type switch
        {
            "quiz" => new
            {
                title = $"{topic}: wybór strategii spójności danych",
                scenarioMarkdown = "Zespół buduje moduł zamówień w modularnym monolicie .NET. Zamówienie i rezerwacja stanów magazynowych muszą być spójne, ale moduł magazynu jest wolny.\n\n**Które podejście jest najlepsze na start?**",
                instructionsMarkdown = "Wybierz jedną odpowiedź i uzasadnij wybór.",
                expectedOutcomeMarkdown = (string?)null,
                rubricMarkdown = "",
                mentorGuideMarkdown = "Zwróć uwagę, czy mentee rozumie koszt rozproszonych transakcji w monolicie.",
                explanationMarkdown = "Jedna transakcja bazodanowa w ramach jednego procesu i jednej bazy jest najprostsza i spójna; outbox/saga są uzasadnione dopiero przy rozdzieleniu procesów.",
                discussionQuestions = new[] { "Kiedy przejście na sagę byłoby uzasadnione?" },
                quizOptions = new[]
                {
                    new { text = "Jedna transakcja w ramach wspólnej bazy", isCorrect = true, explanationMarkdown = "Najprostsze i spójne rozwiązanie w monolicie." },
                    new { text = "Saga z kompensacjami przez event bus", isCorrect = false, explanationMarkdown = "Nadmiarowa złożoność bez rozdzielenia procesów." },
                    new { text = "Two-phase commit między schematami", isCorrect = false, explanationMarkdown = "Kosztowne i zbędne w jednej bazie." },
                },
            },
            "practical" => new
            {
                title = $"{topic}: implementacja idempotentnego endpointu",
                scenarioMarkdown = "Zaimplementuj endpoint przyjmujący płatności, który bezpiecznie obsługuje ponowienia żądań klienta.",
                instructionsMarkdown = "- ASP.NET Core Minimal API\n- PostgreSQL + EF Core\n- Klucz idempotencji w nagłówku",
                expectedOutcomeMarkdown = "Repozytorium z kodem, testami integracyjnymi i krótkim ADR.",
                rubricMarkdown = "- Poprawność idempotencji przy współbieżności\n- Jakość testów\n- Uzasadnienie decyzji w ADR",
                mentorGuideMarkdown = "Sprawdź, czy unikalny indeks chroni przed wyścigiem, a nie tylko sprawdzenie w kodzie.",
                explanationMarkdown = (string?)null,
                discussionQuestions = new[] { "Jak długo przechowujesz klucze idempotencji i dlaczego?", "Co zwracasz przy konflikcie treści żądania?" },
                quizOptions = Array.Empty<object>(),
            },
            _ => new
            {
                title = $"{topic}: decyzja o podziale modułów",
                scenarioMarkdown = "Startup rozwija platformę rezerwacji. Zespół 4 osób, ruch rośnie 20% miesięcznie. CTO proponuje mikroserwisy.",
                instructionsMarkdown = "- Budżet infrastruktury ograniczony\n- Wymagane wdrożenia kilka razy dziennie\n- Jeden zespół",
                expectedOutcomeMarkdown = "Decyzja architektoniczna z uzasadnieniem, alternatywami, kompromisami, ryzykami i warunkami ponownej oceny.",
                rubricMarkdown = "- Rzetelna analiza alternatyw\n- Świadomość kosztów operacyjnych\n- Konkretne warunki rewizji decyzji",
                mentorGuideMarkdown = "Dobra odpowiedź zwykle wskazuje modularny monolit z jasnymi granicami i kryteriami wydzielenia.",
                explanationMarkdown = (string?)null,
                discussionQuestions = new[] { "Jakie metryki sygnalizowałyby potrzebę wydzielenia usługi?" },
                quizOptions = Array.Empty<object>(),
            },
        };

        return new DraftGenerationResponse(true, JsonSerializer.Serialize(payload), "fake-model", 420, 680, null);
    }
}

/// <summary>Provider-agnostic adapter for OpenAI-compatible chat completion APIs with JSON-schema structured output.</summary>
internal sealed class OpenAiCompatibleDraftGenerator(HttpClient http, IOptions<AiOptions> options) : IExerciseDraftGenerator
{
    public string ProviderName => "OpenAiCompatible";

    public async Task<DraftGenerationResponse> GenerateAsync(DraftGenerationPrompt prompt, CancellationToken ct)
    {
        var o = options.Value;
        var body = new JsonObject
        {
            ["model"] = o.Model,
            ["max_completion_tokens"] = prompt.MaxOutputTokens,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = prompt.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = prompt.UserPrompt }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "exercise_draft",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(prompt.JsonSchema),
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return new DraftGenerationResponse(false, null, o.Model, null, null, $"http_{(int)response.StatusCode}");

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(ct);
        var content = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        var usage = json?["usage"];
        return new DraftGenerationResponse(content is not null, content, json?["model"]?.GetValue<string>() ?? o.Model,
            usage?["prompt_tokens"]?.GetValue<int>(), usage?["completion_tokens"]?.GetValue<int>(),
            content is null ? "empty_response" : null);
    }
}
