using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Submissions.Contracts;

public interface ISubmissionService
{
    Task<Result<MenteeAssignmentDto>> GetAssignmentForMenteeAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitQuizAsync(Guid actorUserId, SubmitQuizRequest request, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitDecisionAsync(Guid actorUserId, SubmitDecisionRequest request, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitPracticalAsync(Guid actorUserId, SubmitPracticalRequest request, CancellationToken ct);
    Task<Result<SubmissionHistoryDto>> GetHistoryAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
    Task<Result<AttachmentDownload>> OpenAttachmentAsync(Guid actorUserId, Guid attachmentId, CancellationToken ct);
}

public sealed record DecisionAnswerV1(string Decision, string Rationale, string Alternatives, string TradeOffs,
    string Risks, string ReconsiderationConditions);
public sealed record SubmitQuizRequest(Guid AssignmentId, Guid ClientRequestId, Guid SelectedQuizOptionId, string RationaleMarkdown, int ConfidenceBefore);
public sealed record SubmitDecisionRequest(Guid AssignmentId, Guid ClientRequestId, DecisionAnswerV1 Answer, int ConfidenceBefore);
public sealed record SubmitPracticalRequest(Guid AssignmentId, Guid ClientRequestId, string RationaleMarkdown, int ConfidenceBefore,
    string? ExternalUrl, FileUpload? File);
public sealed record FileUpload(string FileName, string ContentType, long SizeBytes, Stream Content);

public enum SubmitBlockReason { None, MentorshipNotActive, AssignmentInactive, PendingReview, AlreadyCompleted }
public sealed record MenteeAssignmentDto(Guid AssignmentId, Guid MentorshipId, string TopicTitle, bool IsRequired, DateTime? DueAtUtc,
    MenteeExerciseDto Exercise, SubmitBlockReason SubmitBlockReason, int AttemptCount);
public sealed record SubmissionResultDto(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status, bool WasDuplicate, QuizOutcomeDto? Quiz);
public sealed record QuizOutcomeDto(bool IsCorrect, string SelectedOptionExplanationMarkdown, string? ExerciseExplanationMarkdown);

public sealed record SubmissionHistoryDto(Guid AssignmentId, string ExerciseTitle, ExerciseType Type, IReadOnlyList<SubmissionHistoryItem> Attempts);
public sealed record SubmissionHistoryItem(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status, DateTime SubmittedAtUtc,
    int ConfidenceBefore, string? SelectedOptionText, DecisionAnswerV1? DecisionAnswer, string? AnswerMarkdown, string? ExternalUrl,
    AttachmentInfo? Attachment, FeedbackDto? Feedback, ReflectionDto? Reflection, bool CanAddReflection);
public sealed record AttachmentInfo(Guid AttachmentId, string FileName, string ContentType, long SizeBytes);
public sealed record FeedbackDto(ReviewVerdict Verdict, string StrengthsMarkdown, string ImprovementsMarkdown, string ChallengeQuestionMarkdown, DateTime CreatedAtUtc);
public sealed record ReflectionDto(int ConfidenceAfter, string TakeawayMarkdown, DateTime CreatedAtUtc);
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType);
