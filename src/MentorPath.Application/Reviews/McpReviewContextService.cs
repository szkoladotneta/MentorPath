using System.Text.Json;
using MentorPath.Application.Catalog;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace MentorPath.Application.Reviews;

internal sealed class McpReviewContextService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    IOptionsMonitor<McpReviewOptions> options,
    IFileStorage storage,
    ILogger<McpReviewContextService> logger) : IMcpReviewContextService
{
    public async Task<Result<McpReviewContextDto>> GetAsync(Guid actorUserId, Guid mentorshipId, Guid submissionId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is not { Role: AccessRole.WorkspaceOwner }) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await McpReviewAccess.LockAsync(db, acc, ct);
        if (mentorship is null) return Errors.NotFound("review");
        var selected = await McpReviewAccess.Submissions(db, acc).Where(s => s.Id == submissionId)
            .Select(s => new { s.AssignmentId, s.Assignment!.ExerciseId }).SingleOrDefaultAsync(ct);
        if (selected is null) return Errors.NotFound("review");
        var denied = await McpReviewAccess.CheckAsync(db, mentorship, options.CurrentValue, ct);
        if (denied is not null) return denied;
        var exercise = await db.Exercises.AsNoTracking().Include(e => e.QuizOptions).Include(e => e.Topic)
            .SingleAsync(e => e.WorkspaceId == acc.WorkspaceId && e.Id == selected.ExerciseId, ct);
        var criterionIds = await ExerciseAuthoringService.LoadCriterionIdsAsync(db, acc.WorkspaceId,
            selected.ExerciseId, activeOnly: false, ct);
        var criteria = await ReviewService.LoadReviewCriteriaAsync(db, acc.WorkspaceId, selected.ExerciseId, ct);
        var attempts = await LoadAttemptsAsync(db, McpReviewAccess.Submissions(db, acc)
            .Where(s => s.Id == submissionId), acc.WorkspaceId, 1, ct);
        var attempt = attempts.Single();
        return new McpReviewContextDto(mentorshipId, selected.AssignmentId, submissionId, attempt.Status,
            ExerciseAuthoringService.ToMentorDto(exercise, criterionIds), attempt, criteria.Select(RubricService.ToDto).ToList());
    }

    public async Task<Result<McpSubmissionHistoryPage>> GetHistoryAsync(Guid actorUserId, Guid mentorshipId,
        Guid assignmentId, int? beforeAttemptNumber, int pageSize, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is not { Role: AccessRole.WorkspaceOwner }) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await McpReviewAccess.LockAsync(db, acc, ct);
        if (mentorship is null) return Errors.NotFound("review");
        var assignment = await db.Assignments.AsNoTracking()
            .Where(a => a.Id == assignmentId && a.WorkspaceId == acc.WorkspaceId &&
                a.RoadmapItem!.Roadmap!.MentorshipId == mentorshipId)
            .Select(a => new { a.Exercise!.Title, a.Exercise.Type }).SingleOrDefaultAsync(ct);
        if (assignment is null) return Errors.NotFound("review");
        var denied = await McpReviewAccess.CheckAsync(db, mentorship, options.CurrentValue, ct);
        if (denied is not null) return denied;
        if (pageSize == 0) pageSize = McpReviewOptions.DefaultHistoryPageSize;
        if (pageSize < 1 || pageSize > McpReviewOptions.MaxHistoryPageSize || beforeAttemptNumber is <= 0)
            return Errors.Validation("mcp_review.invalid_page", "Nieprawidłowy rozmiar strony lub kursor historii.");
        var attempts = await LoadAttemptsAsync(db, McpReviewAccess.Submissions(db, acc)
            .Where(s => s.AssignmentId == assignmentId && (beforeAttemptNumber == null || s.AttemptNumber < beforeAttemptNumber)),
            acc.WorkspaceId, pageSize + 1, ct);
        var hasMore = attempts.Count > pageSize;
        var page = attempts.Take(pageSize).ToList();
        return new McpSubmissionHistoryPage(assignmentId, assignment.Title, assignment.Type,
            page, hasMore ? page[^1].AttemptNumber : null);
    }

    public async Task<Result<McpAttachmentChunk>> ReadAttachmentAsync(Guid actorUserId, Guid mentorshipId,
        Guid submissionId, Guid attachmentId, long offset, int byteCount, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is not { Role: AccessRole.WorkspaceOwner }) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await McpReviewAccess.LockAsync(db, acc, ct);
        if (mentorship is null) return Errors.NotFound("review");
        var attachment = await db.SubmissionAttachments.AsNoTracking()
            .Where(a => a.Id == attachmentId && a.WorkspaceId == acc.WorkspaceId && a.SubmissionId == submissionId &&
                a.Submission!.Assignment!.RoadmapItem!.Roadmap!.MentorshipId == mentorshipId).SingleOrDefaultAsync(ct);
        if (attachment is null) return Errors.NotFound("review");
        var denied = await McpReviewAccess.CheckAsync(db, mentorship, options.CurrentValue, ct);
        if (denied is not null) return denied;
        if (offset < 0 || offset > attachment.SizeBytes || byteCount < 1 || byteCount > McpReviewOptions.MaxAttachmentChunkBytes)
            return Errors.Validation("mcp_review.invalid_range", "Nieprawidłowy zakres pliku.");
        try
        {
            await using var stream = await storage.OpenReadAsync(attachment.ObjectKey, ct);
            if (stream.CanSeek) stream.Seek(offset, SeekOrigin.Begin);
            else
            {
                var buffer = new byte[8192];
                var remaining = offset;
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
                    if (read == 0) throw new EndOfStreamException();
                    remaining -= read;
                }
            }
            var content = new byte[(int)Math.Min(byteCount, attachment.SizeBytes - offset)];
            await stream.ReadExactlyAsync(content, ct);
            var next = offset + content.Length;
            return new McpAttachmentChunk(attachment.Id, attachment.OriginalFileName, attachment.ContentType,
                attachment.SizeBytes, offset, content, next < attachment.SizeBytes ? next : null);
        }
        catch (FileNotFoundException) { return Errors.NotFound("review"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("MCP attachment read failed. AttachmentId={AttachmentId} FailureType={FailureType}",
                attachment.Id, ex.GetType().Name);
            return new AppError(ErrorKind.Unavailable, "submission.storage_unavailable", "Pobieranie pliku jest chwilowo niedostępne.");
        }
    }

    // Apply the cursor and limit before materializing answers or loading feedback scores.
    private static async Task<List<McpSubmissionAttemptDto>> LoadAttemptsAsync(AppDbContext db,
        IQueryable<Submission> query, Guid workspaceId, int limit, CancellationToken ct)
    {
        var rows = await query.OrderByDescending(s => s.AttemptNumber).Take(limit).Select(s => new
        {
            s.Id, s.AttemptNumber, s.Status, s.SubmittedAtUtc, s.ConfidenceBefore,
            OptionText = s.SelectedQuizOption != null ? s.SelectedQuizOption.Text : null,
            s.DecisionAnswerJson, s.AnswerMarkdown, s.ExternalUrl,
            Attachment = s.Attachment == null ? null : new AttachmentInfo(s.Attachment.Id, s.Attachment.OriginalFileName, s.Attachment.ContentType, s.Attachment.SizeBytes),
            Feedback = s.Feedback == null ? null : new FeedbackDto(s.Feedback.Verdict, s.Feedback.StrengthsMarkdown,
                s.Feedback.ImprovementsMarkdown, s.Feedback.ChallengeQuestionMarkdown, s.Feedback.CreatedAtUtc),
            Reflection = s.Reflection == null ? null : new ReflectionDto(s.Reflection.ConfidenceAfter, s.Reflection.TakeawayMarkdown, s.Reflection.CreatedAtUtc),
        }).ToListAsync(ct);
        var ids = rows.Where(r => r.Feedback != null).Select(r => r.Id).ToList();
        var scoreRows = await db.FeedbackCriterionScores.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId && ids.Contains(s.Feedback!.SubmissionId))
            .OrderBy(s => s.Criterion!.Position).ThenBy(s => s.Criterion!.Name)
            .Select(s => new
            {
                s.Feedback!.SubmissionId, s.CriterionId, s.Criterion!.Name, s.Criterion.Position, s.Score, s.CommentMarkdown,
                s.Criterion.Level1Description, s.Criterion.Level2Description, s.Criterion.Level3Description,
                s.Criterion.Level4Description, s.Criterion.Level5Description,
            }).ToListAsync(ct);
        var scores = scoreRows.GroupBy(s => s.SubmissionId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<FeedbackCriterionScoreDto>)g.Select(s => new FeedbackCriterionScoreDto(
                s.CriterionId, s.Name, s.Position, s.Score, s.CommentMarkdown, s.Score switch
                {
                    1 => s.Level1Description, 2 => s.Level2Description, 3 => s.Level3Description,
                    4 => s.Level4Description, _ => s.Level5Description,
                })).ToList());
        return rows.Select(s => new McpSubmissionAttemptDto(s.Id, s.AttemptNumber, s.Status, s.SubmittedAtUtc,
            s.ConfidenceBefore, s.OptionText, ParseDecision(s.DecisionAnswerJson), s.AnswerMarkdown, s.ExternalUrl,
            s.Attachment, s.Feedback is not null && scores.TryGetValue(s.Id, out var awarded)
                ? s.Feedback with { Scores = awarded } : s.Feedback, s.Reflection)).ToList();
    }

    private static DecisionAnswerV1? ParseDecision(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var version = root.GetProperty("schemaVersion").GetInt32();
        if (version == 2) root = root.GetProperty("answer");
        else if (version != 1) throw new JsonException("Unsupported decision answer schema version.");
        return new(root.GetProperty("decision").GetString()!, root.GetProperty("rationale").GetString()!,
            root.GetProperty("alternatives").GetString()!, root.GetProperty("tradeOffs").GetString()!,
            root.GetProperty("risks").GetString()!, root.GetProperty("reconsiderationConditions").GetString()!);
    }
}
