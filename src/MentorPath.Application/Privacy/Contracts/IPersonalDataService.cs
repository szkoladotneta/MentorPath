using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Privacy.Contracts;

/// <summary>
/// Operator-only personal data procedures (GDPR minimum, N04), executed from the CLI. There is no logged-in actor,
/// so these methods are exempt from the `actorUserId` convention (like <c>IMentorProvisioningService</c>), and only
/// CLI commands may use this service (<c>ConventionTests.Operator_services_are_used_only_by_the_CLI</c>).
/// Only mentees can be subjects; workspace owners are refused.
/// Error codes: <c>privacy.not_found</c> (NotFound), <c>privacy.invalid_request</c> (Validation: exactly one of UserId / Email),
/// <c>privacy.unsupported_subject</c> (Validation: workspace owner), <c>privacy.storage_failed</c> (Unavailable: file deletion failed, database unchanged).
/// </summary>
public interface IPersonalDataService
{
    /// <summary>Read-only export of everything stored about the subject. Contains attachment keys, never file content.</summary>
    Task<Result<PersonalDataExport>> ExportAsync(PersonalDataSubject subject, CancellationToken ct);

    /// <summary>
    /// Removes PII and files while keeping workspace statistics. <paramref name="apply"/> = false is a dry run that only reports counts.
    /// Idempotent and safe to re-run: the account is anonymized once, while content scrubbing and file deletion always run
    /// (they also catch feedback written after the first run). <see cref="AnonymizationReport.AlreadyAnonymized"/> is true only when
    /// the account e-mail is exactly <c>deleted-{UserId:N}@anonymized.invalid</c> for this user, never on a domain match.
    /// </summary>
    Task<Result<AnonymizationReport>> AnonymizeAsync(PersonalDataSubject subject, bool apply, CancellationToken ct);
}

/// <summary>Identifies the person by exactly one of <paramref name="UserId"/> or <paramref name="Email"/>.</summary>
public sealed record PersonalDataSubject(Guid? UserId, string? Email);

public sealed record PersonalDataExport(
    int SchemaVersion,
    DateTime GeneratedAtUtc,
    ProfileExport Profile,
    IReadOnlyList<MentorshipExport> Mentorships,
    IReadOnlyList<SubmissionExport> Submissions,
    IReadOnlyList<InvitationExport> Invitations);

public sealed record ProfileExport(Guid UserId, string Email, string DisplayName, DateTime CreatedAtUtc, IReadOnlyList<string> Roles);

public sealed record MentorshipExport(
    Guid MentorshipId, string WorkspaceName, string? Goal, ExperienceLevel? CurrentLevel, int? WeeklyHours,
    string? Challenges, MentorshipStatus Status, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record SubmissionExport(
    Guid SubmissionId, Guid MentorshipId, string TopicTitle, string ExerciseTitle, ExerciseType ExerciseType,
    int AttemptNumber, SubmissionStatus Status, string? SelectedQuizOptionText, string? DecisionAnswerJson,
    string? AnswerMarkdown, string? ExternalUrl, short ConfidenceBefore, DateTime SubmittedAtUtc,
    AttachmentExport? Attachment, FeedbackExport? Feedback, ReflectionExport? Reflection);

public sealed record AttachmentExport(string ObjectKey, string OriginalFileName, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

public sealed record FeedbackExport(
    ReviewVerdict Verdict, string StrengthsMarkdown, string ImprovementsMarkdown, string ChallengeQuestionMarkdown, DateTime CreatedAtUtc);

public sealed record ReflectionExport(short ConfidenceAfter, string TakeawayMarkdown, DateTime CreatedAtUtc);

public sealed record InvitationExport(Guid InvitationId, string WorkspaceName, DateTime ExpiresAtUtc, DateTime? AcceptedAtUtc, DateTime CreatedAtUtc);

/// <summary>Counts only (no e-mails, texts or file names). In a dry run they describe what would change.</summary>
public sealed record AnonymizationReport(
    Guid UserId, bool Applied, bool AlreadyAnonymized,
    int MentorshipsClosed, int MentorshipsScrubbed, int SubmissionsScrubbed, int AttachmentsDeleted,
    int FeedbackScrubbed, int ReflectionsScrubbed, int InvitationsScrubbed);
