using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Reviews.Contracts;

// All reads require the owning mentor and current consent for the exact mentorship.
public interface IMcpReviewContextService
{
    Task<Result<McpReviewContextDto>> GetAsync(Guid actorUserId, Guid mentorshipId, Guid submissionId, CancellationToken ct);
    Task<Result<McpSubmissionHistoryPage>> GetHistoryAsync(Guid actorUserId, Guid mentorshipId,
        Guid assignmentId, int? beforeAttemptNumber, int pageSize, CancellationToken ct);
    Task<Result<McpAttachmentChunk>> ReadAttachmentAsync(Guid actorUserId, Guid mentorshipId,
        Guid submissionId, Guid attachmentId, long offset, int byteCount, CancellationToken ct);
}

public sealed record McpReviewContextDto(Guid MentorshipId, Guid AssignmentId, Guid SubmissionId,
    SubmissionStatus Status, MentorExerciseDto Exercise, McpSubmissionAttemptDto Submission,
    IReadOnlyList<RubricCriterionDto> Criteria);
public sealed record McpSubmissionHistoryPage(Guid AssignmentId, string ExerciseTitle, ExerciseType Type,
    IReadOnlyList<McpSubmissionAttemptDto> Attempts, int? NextBeforeAttemptNumber);
public sealed record McpSubmissionAttemptDto(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status,
    DateTime SubmittedAtUtc, int ConfidenceBefore, string? SelectedOptionText, DecisionAnswerV1? DecisionAnswer,
    string? AnswerMarkdown, string? ExternalUrl, AttachmentInfo? Attachment, FeedbackDto? Feedback, ReflectionDto? Reflection);
public sealed record McpAttachmentChunk(Guid AttachmentId, string FileName, string ContentType,
    long SizeBytes, long Offset, byte[] Content, long? NextOffset);
