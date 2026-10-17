using System.Text.Json;
using MentorPath.Application.Catalog;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MentorPath.Application.Submissions;

internal sealed class SubmissionService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    IFileStorage storage,
    TimeProvider clock,
    ILogger<SubmissionService> logger) : ISubmissionService
{
    private const string Module = "submission";

    internal static readonly JsonSerializerOptions DecisionJson = new(JsonSerializerDefaults.Web);

    private sealed record DecisionAnswerDocument(int SchemaVersion, string Decision, string Rationale, string Alternatives,
        string TradeOffs, string Risks, string ReconsiderationConditions);

    public async Task<Result<MenteeAssignmentDto>> GetAssignmentForMenteeAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct)
    {
        var acc = await access.GetAssignmentAccessAsync(actorUserId, assignmentId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var a = await db.Assignments.AsNoTracking()
            .Include(x => x.Exercise!).ThenInclude(e => e.QuizOptions)
            .Include(x => x.RoadmapItem!).ThenInclude(i => i.Topic)
            .FirstAsync(x => x.Id == assignmentId && x.WorkspaceId == acc.WorkspaceId, ct);
        var statuses = await db.Submissions.AsNoTracking()
            .Where(s => s.AssignmentId == assignmentId && s.WorkspaceId == acc.WorkspaceId)
            .Select(s => s.Status).ToListAsync(ct);

        return new MenteeAssignmentDto(a.Id, acc.MentorshipId, a.RoadmapItem!.Topic!.Title, a.IsRequired, a.DueAtUtc,
            ExerciseAuthoringService.ToMenteeDto(a.Exercise!), BlockReason(acc, statuses), statuses.Count);
    }

    public async Task<Result<SubmissionResultDto>> SubmitQuizAsync(Guid actorUserId, SubmitQuizRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        errors.Required(nameof(request.RationaleMarkdown), request.RationaleMarkdown, InputLimits.Markdown, "Uzasadnienie");
        errors.Confidence(nameof(request.ConfidenceBefore), request.ConfidenceBefore);
        if (request.SelectedQuizOptionId == Guid.Empty) errors.Add(nameof(request.SelectedQuizOptionId), "Wybierz odpowiedź.");
        if (errors.HasErrors) return errors.ToError("submission.invalid");

        var pre = await PrepareAsync(actorUserId, request.AssignmentId, request.ClientRequestId, ExerciseType.QuizSingleChoice, ct);
        if (pre.Error is not null) return pre.Error;
        if (pre.Duplicate is not null) return pre.Duplicate;
        var (acc, exercise) = (pre.Access!, pre.Exercise!);

        var option = exercise.QuizOptions.FirstOrDefault(o => o.Id == request.SelectedQuizOptionId);
        if (option is null)
            return Errors.Validation("submission.invalid_option", "Formularz zawiera błędy.",
                new Dictionary<string, string[]> { [nameof(request.SelectedQuizOptionId)] = ["Wybierz jedną z dostępnych odpowiedzi."] });

        var submission = NewSubmission(acc, actorUserId, request.ClientRequestId, request.ConfidenceBefore);
        submission.Status = option.IsCorrect ? SubmissionStatus.Correct : SubmissionStatus.Incorrect;
        submission.SelectedQuizOptionId = option.Id;
        submission.AnswerMarkdown = request.RationaleMarkdown.Trim();

        var saved = await InsertAsync(submission, request.AssignmentId, null, ct);
        if (!saved.IsSuccess) return saved.Error!;
        var dto = saved.Value!;
        if (dto.WasDuplicate) return dto;
        return dto with
        {
            Quiz = new QuizOutcomeDto(option.IsCorrect, option.ExplanationMarkdown, option.IsCorrect ? exercise.ExplanationMarkdown : null),
        };
    }

    public async Task<Result<SubmissionResultDto>> SubmitDecisionAsync(Guid actorUserId, SubmitDecisionRequest request, CancellationToken ct)
    {
        var a = request.Answer;
        var errors = new FieldErrors();
        if (a is null) errors.Add(nameof(request.Answer), "Uzupełnij odpowiedź.");
        else
        {
            errors.Required("Answer.Decision", a.Decision, InputLimits.Markdown, "Decyzja");
            errors.Required("Answer.Rationale", a.Rationale, InputLimits.Markdown, "Uzasadnienie");
            errors.Required("Answer.Alternatives", a.Alternatives, InputLimits.Markdown, "Alternatywy");
            errors.Required("Answer.TradeOffs", a.TradeOffs, InputLimits.Markdown, "Kompromisy");
            errors.Required("Answer.Risks", a.Risks, InputLimits.Markdown, "Ryzyka");
            errors.Required("Answer.ReconsiderationConditions", a.ReconsiderationConditions, InputLimits.Markdown, "Warunki ponownej oceny");
        }
        errors.Confidence(nameof(request.ConfidenceBefore), request.ConfidenceBefore);
        if (errors.HasErrors) return errors.ToError("submission.invalid");

        var pre = await PrepareAsync(actorUserId, request.AssignmentId, request.ClientRequestId, ExerciseType.ArchitecturalDecision, ct);
        if (pre.Error is not null) return pre.Error;
        if (pre.Duplicate is not null) return pre.Duplicate;

        var submission = NewSubmission(pre.Access!, actorUserId, request.ClientRequestId, request.ConfidenceBefore);
        submission.Status = SubmissionStatus.PendingReview;
        submission.DecisionAnswerJson = JsonSerializer.Serialize(new DecisionAnswerDocument(1, a!.Decision.Trim(), a.Rationale.Trim(),
            a.Alternatives.Trim(), a.TradeOffs.Trim(), a.Risks.Trim(), a.ReconsiderationConditions.Trim()), DecisionJson);
        return await InsertAsync(submission, request.AssignmentId, null, ct);
    }

    public async Task<Result<SubmissionResultDto>> SubmitPracticalAsync(Guid actorUserId, SubmitPracticalRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        errors.Required(nameof(request.RationaleMarkdown), request.RationaleMarkdown, InputLimits.Markdown, "Uzasadnienie decyzji");
        errors.Confidence(nameof(request.ConfidenceBefore), request.ConfidenceBefore);
        var url = Text.NullIfBlank(request.ExternalUrl);
        if (url is null && request.File is null)
            errors.Add(nameof(request.ExternalUrl), "Podaj link do repozytorium/artefaktu lub dołącz plik.");
        if (url is not null && (url.Length > InputLimits.ExternalUrl || !FieldErrors.IsHttpUrl(url)))
            errors.Add(nameof(request.ExternalUrl), "Podaj poprawny adres http(s).");

        byte[]? fileBytes = null;
        if (request.File is not null && !errors.HasErrors)
        {
            if (request.File.SizeBytes > InputLimits.MaxAttachmentBytes)
                errors.Add(nameof(request.File), "Plik może mieć maksymalnie 10 MB.");
            else
            {
                fileBytes = await ReadLimitedAsync(request.File.Content, ct);
                var fileError = fileBytes is null
                    ? "Plik może mieć maksymalnie 10 MB."
                    : AttachmentValidator.Validate(request.File.FileName, request.File.ContentType, fileBytes);
                if (fileError is not null) errors.Add(nameof(request.File), fileError);
            }
        }
        if (errors.HasErrors) return errors.ToError("submission.invalid");

        var pre = await PrepareAsync(actorUserId, request.AssignmentId, request.ClientRequestId, ExerciseType.PracticalTask, ct);
        if (pre.Error is not null) return pre.Error;
        if (pre.Duplicate is not null) return pre.Duplicate;
        var acc = pre.Access!;

        var submission = NewSubmission(acc, actorUserId, request.ClientRequestId, request.ConfidenceBefore);
        submission.Status = SubmissionStatus.PendingReview;
        submission.AnswerMarkdown = request.RationaleMarkdown.Trim();
        submission.ExternalUrl = url;

        SubmissionAttachment? attachment = null;
        if (fileBytes is not null)
        {
            var fileName = AttachmentValidator.SafeFileName(request.File!.FileName);
            attachment = new SubmissionAttachment
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = acc.WorkspaceId,
                SubmissionId = submission.Id,
                ObjectKey = $"ws/{acc.WorkspaceId:N}/sub/{Guid.NewGuid():N}",
                OriginalFileName = fileName,
                ContentType = AttachmentValidator.CanonicalContentType(fileName),
                SizeBytes = fileBytes.LongLength,
                CreatedAtUtc = submission.SubmittedAtUtc,
            };
            using var ms = new MemoryStream(fileBytes, writable: false);
            await storage.SaveAsync(attachment.ObjectKey, ms, attachment.ContentType, ct);
        }

        Result<SubmissionResultDto> result;
        try
        {
            result = await InsertAsync(submission, request.AssignmentId, attachment, ct);
        }
        catch
        {
            if (attachment is not null) await TryDeleteAsync(attachment.ObjectKey);
            throw;
        }
        if (attachment is not null && (!result.IsSuccess || result.Value!.WasDuplicate))
            await TryDeleteAsync(attachment.ObjectKey);
        return result;
    }

    public async Task<Result<SubmissionHistoryDto>> GetHistoryAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct)
    {
        var acc = await access.GetAssignmentAccessAsync(actorUserId, assignmentId, ct);
        if (acc is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadHistoryAsync(db, acc, ct);
    }

    internal static async Task<SubmissionHistoryDto> LoadHistoryAsync(AppDbContext db, AssignmentAccess acc, CancellationToken ct)
    {
        var exercise = await db.Exercises.AsNoTracking()
            .Where(e => e.Id == acc.ExerciseId && e.WorkspaceId == acc.WorkspaceId)
            .Select(e => new { e.Title, e.Type }).FirstAsync(ct);
        var rows = await db.Submissions.AsNoTracking()
            .Where(s => s.AssignmentId == acc.AssignmentId && s.WorkspaceId == acc.WorkspaceId)
            .OrderByDescending(s => s.AttemptNumber)
            .Select(s => new
            {
                s.Id, s.AttemptNumber, s.Status, s.SubmittedAtUtc, s.ConfidenceBefore,
                OptionText = s.SelectedQuizOption != null ? s.SelectedQuizOption.Text : null,
                s.DecisionAnswerJson, s.AnswerMarkdown, s.ExternalUrl,
                Attachment = s.Attachment == null ? null : new AttachmentInfo(s.Attachment.Id, s.Attachment.OriginalFileName, s.Attachment.ContentType, s.Attachment.SizeBytes),
                Feedback = s.Feedback == null ? null : new FeedbackDto(s.Feedback.Verdict, s.Feedback.StrengthsMarkdown, s.Feedback.ImprovementsMarkdown, s.Feedback.ChallengeQuestionMarkdown, s.Feedback.CreatedAtUtc),
                Reflection = s.Reflection == null ? null : new ReflectionDto(s.Reflection.ConfidenceAfter, s.Reflection.TakeawayMarkdown, s.Reflection.CreatedAtUtc),
            })
            .ToListAsync(ct);

        var items = rows.Select(s => new SubmissionHistoryItem(s.Id, s.AttemptNumber, s.Status, s.SubmittedAtUtc, s.ConfidenceBefore,
                s.OptionText, ParseDecision(s.DecisionAnswerJson), s.AnswerMarkdown, s.ExternalUrl, s.Attachment, s.Feedback, s.Reflection,
                acc.Role == AccessRole.Mentee && s.Reflection is null && CanReflectOn(s.Status)))
            .ToList();
        return new SubmissionHistoryDto(acc.AssignmentId, exercise.Title, exercise.Type, items);
    }

    internal static bool CanReflectOn(SubmissionStatus status) => status is SubmissionStatus.Correct or SubmissionStatus.Incorrect
        or SubmissionStatus.Accepted or SubmissionStatus.NeedsRevision;

    public async Task<Result<AttachmentDownload>> OpenAttachmentAsync(Guid actorUserId, Guid attachmentId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attachment = await db.SubmissionAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null || !await access.CanAccessSubmissionAsync(actorUserId, attachment.SubmissionId, ct))
            return Errors.NotFound(Module);
        try
        {
            var stream = await storage.OpenReadAsync(attachment.ObjectKey, ct);
            return new AttachmentDownload(stream, attachment.OriginalFileName, attachment.ContentType);
        }
        catch (FileNotFoundException)
        {
            logger.LogError("Attachment object missing in storage. AttachmentId={AttachmentId}", attachment.Id);
            return Errors.NotFound(Module);
        }
    }

    private sealed record Prepared(AssignmentAccess? Access, Exercise? Exercise, SubmissionResultDto? Duplicate, AppError? Error);

    private async Task<Prepared> PrepareAsync(Guid actorUserId, Guid assignmentId, Guid clientRequestId, ExerciseType expectedType, CancellationToken ct)
    {
        if (clientRequestId == Guid.Empty)
            return new(null, null, null, Errors.Validation("submission.missing_client_request_id", "Brak identyfikatora żądania."));
        var acc = await access.GetAssignmentAccessAsync(actorUserId, assignmentId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return new(null, null, null, Errors.NotFound(Module));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var duplicate = await FindDuplicateAsync(db, actorUserId, clientRequestId, assignmentId, ct);
        if (duplicate is not null) return duplicate;

        var exercise = await db.Exercises.AsNoTracking().Include(e => e.QuizOptions)
            .FirstAsync(e => e.Id == acc.ExerciseId && e.WorkspaceId == acc.WorkspaceId, ct);
        if (exercise.Type != expectedType)
            return new(null, null, null, Errors.Validation("submission.wrong_type", "Ten formularz nie pasuje do typu ćwiczenia."));

        var statuses = await db.Submissions.AsNoTracking().Where(s => s.AssignmentId == assignmentId).Select(s => s.Status).ToListAsync(ct);
        var block = BlockReason(acc, statuses);
        if (block != SubmitBlockReason.None) return new(null, null, null, BlockError(block));
        return new(acc, exercise, null, null);
    }

    private static async Task<Prepared?> FindDuplicateAsync(AppDbContext db, Guid menteeUserId, Guid clientRequestId, Guid assignmentId, CancellationToken ct)
    {
        var existing = await db.Submissions.AsNoTracking()
            .Where(s => s.MenteeUserId == menteeUserId && s.ClientRequestId == clientRequestId)
            .Select(s => new { s.Id, s.AssignmentId, s.AttemptNumber, s.Status })
            .FirstOrDefaultAsync(ct);
        if (existing is null) return null;
        if (existing.AssignmentId != assignmentId)
            return new(null, null, null, Errors.Conflict("submission.client_request_reused", "Identyfikator żądania został już użyty."));
        return new(null, null, new SubmissionResultDto(existing.Id, existing.AttemptNumber, existing.Status, true, null), null);
    }

    internal static SubmitBlockReason BlockReason(AssignmentAccess acc, IReadOnlyCollection<SubmissionStatus> statuses)
    {
        if (acc.MentorshipStatus != MentorshipStatus.Active) return SubmitBlockReason.MentorshipNotActive;
        if (!acc.AssignmentIsActive || !acc.RoadmapItemIsActive) return SubmitBlockReason.AssignmentInactive;
        if (statuses.Any(s => s is SubmissionStatus.Correct or SubmissionStatus.Accepted)) return SubmitBlockReason.AlreadyCompleted;
        if (statuses.Contains(SubmissionStatus.PendingReview)) return SubmitBlockReason.PendingReview;
        return SubmitBlockReason.None;
    }

    private static AppError BlockError(SubmitBlockReason reason) => reason switch
    {
        SubmitBlockReason.MentorshipNotActive => Errors.Conflict("submission.mentorship_not_active", "Mentoring nie jest aktywny — nie można wysyłać nowych prób."),
        SubmitBlockReason.AssignmentInactive => Errors.Conflict("submission.assignment_inactive", "To zadanie zostało wycofane z aktywnej ścieżki."),
        SubmitBlockReason.AlreadyCompleted => Errors.Conflict("submission.already_completed", "To zadanie jest już zaliczone."),
        _ => Errors.Conflict("submission.pending_review_exists", "Poprzednia próba czeka na ocenę mentora."),
    };

    private Submission NewSubmission(AssignmentAccess acc, Guid menteeUserId, Guid clientRequestId, int confidence) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkspaceId = acc.WorkspaceId,
        AssignmentId = acc.AssignmentId,
        MenteeUserId = menteeUserId,
        ClientRequestId = clientRequestId,
        ConfidenceBefore = (short)confidence,
        SubmittedAtUtc = clock.GetUtcNow().UtcDateTime,
    };

    private async Task<Result<SubmissionResultDto>> InsertAsync(Submission submission, Guid assignmentId, SubmissionAttachment? attachment, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var max = await db.Submissions.Where(s => s.AssignmentId == assignmentId).MaxAsync(s => (int?)s.AttemptNumber, ct) ?? 0;
        submission.AttemptNumber = max + 1;
        db.Submissions.Add(submission);
        if (attachment is not null) db.SubmissionAttachments.Add(attachment);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            await using var fresh = await dbFactory.CreateDbContextAsync(ct);
            var dup = await FindDuplicateAsync(fresh, submission.MenteeUserId, submission.ClientRequestId, assignmentId, ct);
            if (dup?.Duplicate is not null) return dup.Duplicate;
            if (dup?.Error is not null) return dup.Error;
            return Errors.Conflict("submission.concurrent_attempt", "Inna próba została właśnie wysłana. Odśwież stronę.");
        }
        return new SubmissionResultDto(submission.Id, submission.AttemptNumber, submission.Status, false, null);
    }

    private static DecisionAnswerV1? ParseDecision(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var doc = JsonSerializer.Deserialize<DecisionAnswerDocument>(json, DecisionJson);
        return doc is null ? null : new DecisionAnswerV1(doc.Decision, doc.Rationale, doc.Alternatives, doc.TradeOffs, doc.Risks, doc.ReconsiderationConditions);
    }

    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > InputLimits.MaxAttachmentBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private async Task TryDeleteAsync(string objectKey)
    {
        try
        {
            await storage.DeleteAsync(objectKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete orphaned attachment object.");
        }
    }
}
