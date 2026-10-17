using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Reviews.Contracts;

public interface IReviewService
{
    Task<Result<IReadOnlyList<PendingReviewItem>>> GetPendingAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<ReviewDetailDto>> GetReviewDetailAsync(Guid actorUserId, Guid submissionId, CancellationToken ct);
    Task<Result<Guid>> SubmitFeedbackAsync(Guid actorUserId, SubmitFeedbackRequest request, CancellationToken ct);
}

public sealed record PendingReviewItem(Guid SubmissionId, Guid MentorshipId, string MenteeDisplayName, string ExerciseTitle,
    ExerciseType Type, int AttemptNumber, DateTime SubmittedAtUtc, DateTime? DueAtUtc);
public sealed record ReviewDetailDto(Guid SubmissionId, Guid MentorshipId, string MenteeDisplayName, SubmissionStatus Status,
    MentorExerciseDto Exercise, SubmissionHistoryDto History);
public sealed record SubmitFeedbackRequest(Guid SubmissionId, string StrengthsMarkdown, string ImprovementsMarkdown,
    string ChallengeQuestionMarkdown, ReviewVerdict Verdict);

public interface IReflectionService
{
    Task<Result> SaveReflectionAsync(Guid actorUserId, SaveReflectionRequest request, CancellationToken ct);
}

public sealed record SaveReflectionRequest(Guid SubmissionId, int ConfidenceAfter, string TakeawayMarkdown);
