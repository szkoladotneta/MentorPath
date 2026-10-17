using MentorPath.Application.AiAuthoring.Contracts;
using MentorPath.Application.Catalog;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.AiAuthoring;

internal sealed class AiExerciseDraftService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    IExerciseDraftGenerator generator,
    ExerciseAuthoringService authoring,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<AiExerciseDraftService> logger) : IAiExerciseDraftService
{
    private const string Module = "ai";

    public async Task<Result<AiQuotaDto>> GetQuotaAsync(Guid actorUserId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (start, end) = Period();
        var used = await CountUsedAsync(db, ws.Value, start, ct);
        return new AiQuotaDto(used, options.Value.MonthlyGenerationLimitPerWorkspace, end);
    }

    public async Task<Result<AiDraftResult>> GenerateDraftAsync(Guid actorUserId, GenerateExerciseDraftRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);

        var errors = new FieldErrors();
        if (!Enum.IsDefined(request.Type)) errors.Add(nameof(request.Type), "Wybierz typ ćwiczenia.");
        if (!Enum.IsDefined(request.Difficulty)) errors.Add(nameof(request.Difficulty), "Wybierz poziom trudności.");
        errors.Required(nameof(request.LearningObjectives), request.LearningObjectives, 2000, "Cele edukacyjne");
        errors.Optional(nameof(request.ContextAndConstraints), request.ContextAndConstraints, 4000, "Kontekst i ograniczenia");
        if (request.EstimatedMinutes is not null) errors.Range(nameof(request.EstimatedMinutes), request.EstimatedMinutes, 1, 600, "Szacowany czas");
        if (errors.HasErrors) return errors.ToError("ai.invalid_request");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TopicId && t.WorkspaceId == ws, ct);
        if (topic is null) return Errors.NotFound(Module);
        if (topic.IsArchived) return Errors.Validation("catalog.topic_archived", "Nie można generować ćwiczeń dla zarchiwizowanego tematu.");

        var o = options.Value;
        var (start, _) = Period();
        if (await CountUsedAsync(db, ws.Value, start, ct) >= o.MonthlyGenerationLimitPerWorkspace)
            return new AppError(ErrorKind.QuotaExceeded, "ai.quota_exceeded", "Wykorzystano miesięczny limit generowań AI dla tego workspace.");

        var aiRequest = new AiGenerationRequest
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            RequestedByUserId = actorUserId,
            ExerciseType = request.Type,
            Status = AiRequestStatus.Pending,
            Provider = generator.ProviderName,
            Model = o.Model,
            PromptTemplateVersion = ExerciseDraftPrompt.Version,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.AiGenerationRequests.Add(aiRequest);
        await db.SaveChangesAsync(ct);

        var prompt = new DraftGenerationPrompt(
            ExerciseDraftPrompt.Version,
            ExerciseDraftPrompt.SystemPrompt,
            ExerciseDraftPrompt.BuildUserPrompt(topic.Title, topic.DescriptionMarkdown, topic.LearningObjectivesMarkdown,
                request.Type, request.Difficulty, request.LearningObjectives.Trim(), request.ContextAndConstraints?.Trim(), request.EstimatedMinutes),
            ExerciseDraftPrompt.JsonSchema,
            o.MaxOutputTokens);

        DraftGenerationResponse response;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds)));
            try
            {
                response = await generator.GenerateAsync(prompt, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                response = new DraftGenerationResponse(false, null, o.Model, null, null, "timeout");
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
            {
                response = new DraftGenerationResponse(false, null, o.Model, null, null, "provider_error");
            }
        }

        aiRequest.Model = Truncate(response.Model, 100);
        aiRequest.InputTokens = response.InputTokens;
        aiRequest.OutputTokens = response.OutputTokens;

        if (!response.Success)
        {
            await FailAsync(db, aiRequest, response.ErrorCode ?? "provider_error", ct);
            return new AppError(ErrorKind.Unavailable, "ai.provider_unavailable",
                "Usługa AI jest chwilowo niedostępna. Możesz kontynuować tworzenie ćwiczenia ręcznie.");
        }

        var (content, parseError) = AiDraftValidator.Parse(request.Type, response.RawJson, request.EstimatedMinutes);
        if (content is null)
        {
            await FailAsync(db, aiRequest, parseError ?? "invalid_output", ct);
            return Errors.Validation("ai.invalid_output", "AI zwróciło niepoprawny szkic. Spróbuj ponownie lub utwórz ćwiczenie ręcznie.");
        }

        var created = await authoring.CreateDraftAsync(actorUserId, new CreateExerciseDraftRequest(topic.Id, request.Type, content), ExerciseSource.AI, ct);
        if (!created.IsSuccess)
        {
            await FailAsync(db, aiRequest, "draft_rejected", ct);
            return created.Error!;
        }

        aiRequest.Status = AiRequestStatus.Completed;
        aiRequest.ExerciseId = created.Value;
        aiRequest.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("AI draft generated. Provider={Provider} Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens}",
            aiRequest.Provider, aiRequest.Model, aiRequest.InputTokens, aiRequest.OutputTokens);
        return new AiDraftResult(created.Value, aiRequest.Id);
    }

    private async Task FailAsync(AppDbContext db, AiGenerationRequest aiRequest, string errorCode, CancellationToken ct)
    {
        aiRequest.Status = AiRequestStatus.Failed;
        aiRequest.ErrorCode = Truncate(errorCode, 100);
        aiRequest.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogWarning("AI draft generation failed. Provider={Provider} Model={Model} ErrorCode={ErrorCode}",
            aiRequest.Provider, aiRequest.Model, aiRequest.ErrorCode);
    }

    private static Task<int> CountUsedAsync(AppDbContext db, Guid workspaceId, DateTime periodStart, CancellationToken ct) =>
        db.AiGenerationRequests.CountAsync(r => r.WorkspaceId == workspaceId && r.CreatedAtUtc >= periodStart
            && (r.Status == AiRequestStatus.Pending || r.Status == AiRequestStatus.Completed), ct);

    private (DateTime Start, DateTime End) Period()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
