using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.AiAuthoring.Contracts;

public interface IAiExerciseDraftService
{
    Task<Result<AiDraftResult>> GenerateDraftAsync(Guid actorUserId, GenerateExerciseDraftRequest request, CancellationToken ct);
    Task<Result<AiQuotaDto>> GetQuotaAsync(Guid actorUserId, CancellationToken ct);
}

public sealed record GenerateExerciseDraftRequest(Guid TopicId, ExerciseType Type, Difficulty Difficulty,
    string LearningObjectives, string? ContextAndConstraints, int? EstimatedMinutes);
public sealed record AiDraftResult(Guid ExerciseId, Guid AiRequestId);
public sealed record AiQuotaDto(int Used, int Limit, DateTime PeriodEndsAtUtc);

public interface IExerciseDraftGenerator
{
    string ProviderName { get; }
    Task<DraftGenerationResponse> GenerateAsync(DraftGenerationPrompt prompt, CancellationToken ct);
}

public sealed record DraftGenerationPrompt(string PromptTemplateVersion, string SystemPrompt, string UserPrompt, string JsonSchema, int MaxOutputTokens);
public sealed record DraftGenerationResponse(bool Success, string? RawJson, string Model, int? InputTokens, int? OutputTokens, string? ErrorCode);
