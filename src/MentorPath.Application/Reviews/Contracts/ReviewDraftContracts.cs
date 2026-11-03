using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Reviews.Contracts;

// These contracts are declared before their feature implementations; only the human UI may publish.
public interface IReviewDraftService
{
    Task<Result<ReviewDraftDto?>> GetAsync(Guid actorUserId, Guid submissionId, CancellationToken ct);
    Task<Result<ReviewDraftDto>> SaveAsync(Guid actorUserId, SaveReviewDraftRequest request, CancellationToken ct);
    Task<Result<Guid>> PublishAsync(Guid actorUserId, Guid submissionId, long expectedVersion, CancellationToken ct);
}

public interface IMcpReviewDraftService
{
    Task<Result<ReviewDraftDto?>> GetAsync(Guid actorUserId, Guid mentorshipId, Guid submissionId, CancellationToken ct);
    Task<Result<ReviewDraftDto>> SaveAsync(Guid actorUserId, Guid mentorshipId, SaveReviewDraftRequest request, CancellationToken ct);
}

public sealed record ReviewDraftContent(string? StrengthsMarkdown, string? ImprovementsMarkdown,
    string? ChallengeQuestionMarkdown, ReviewVerdict? Verdict, IReadOnlyList<CriterionScoreInput> Scores);
public sealed record ReviewDraftDto(Guid DraftId, Guid SubmissionId, ReviewDraftContent Content,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version);
// Null ExpectedVersion means create only; updates must match the persisted version.
public sealed record SaveReviewDraftRequest(Guid SubmissionId, ReviewDraftContent Content, long? ExpectedVersion);
