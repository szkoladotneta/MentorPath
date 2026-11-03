using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Reviews;

internal sealed class ReviewDraftService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IReviewDraftService
{
    public async Task<Result<ReviewDraftDto?>> GetAsync(Guid actorUserId, Guid submissionId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.Submissions.AsNoTracking().AnyAsync(s => s.Id == submissionId && s.WorkspaceId == ws, ct))
            return Errors.NotFound("review");
        return Result<ReviewDraftDto?>.Success(await LoadAsync(db, ws.Value, submissionId, ct));
    }

    public async Task<Result<ReviewDraftDto>> SaveAsync(Guid actorUserId, SaveReviewDraftRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await LockMentorshipAsync(db, ws.Value, request.SubmissionId, ct);
        if (mentorship is null) return Errors.NotFound("review");
        if (mentorship.Status == MentorshipStatus.Closed) return MentorshipClosed();
        var submission = await LockSubmissionAsync(db, ws.Value, request.SubmissionId, ct);
        if (submission is null) return Errors.NotFound("review");
        var result = await SaveAsync(db, submission, request, clock, ct);
        if (result.IsSuccess) await tx.CommitAsync(ct);
        return result;
    }

    public async Task<Result<Guid>> PublishAsync(Guid actorUserId, Guid submissionId, long expectedVersion, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await LockMentorshipAsync(db, ws.Value, submissionId, ct);
        if (mentorship is null) return Errors.NotFound("review");
        if (mentorship.Status == MentorshipStatus.Closed) return MentorshipClosed();
        var submission = await LockSubmissionAsync(db, ws.Value, submissionId, ct);
        if (submission is null) return Errors.NotFound("review");
        var draft = await LoadAsync(db, ws.Value, submissionId, ct);
        if (expectedVersion <= 0 || draft is null || draft.Version != expectedVersion) return VersionConflict();
        var content = draft.Content;
        if (content.Verdict is null)
            return Errors.Validation("review.invalid", "Wybierz werdykt.");
        var result = await ReviewService.PublishAsync(db, ws.Value, actorUserId, submission,
            new(submissionId, content.StrengthsMarkdown!, content.ImprovementsMarkdown!,
                content.ChallengeQuestionMarkdown!, content.Verdict.Value, content.Scores), clock, ct);
        if (result.IsSuccess) await tx.CommitAsync(ct);
        return result;
    }

    internal static async Task<Mentorship?> LockMentorshipAsync(AppDbContext db, Guid workspaceId,
        Guid submissionId, CancellationToken ct)
    {
        var mentorshipId = await db.Submissions.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId && s.Id == submissionId)
            .Select(s => (Guid?)s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId).SingleOrDefaultAsync(ct);
        if (mentorshipId is null) return null;
        return await db.Mentorships.FromSqlInterpolated(
                $"""SELECT * FROM "Mentorships" WHERE "Id" = {mentorshipId.Value} AND "WorkspaceId" = {workspaceId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
    }

    internal static AppError MentorshipClosed() =>
        Errors.Conflict("review.mentorship_closed", "Mentoring jest zamknięty — dostępny jest tylko odczyt.");

    // Lock the mentorship first so closure and privacy erasure also serialize human writes.
    internal static Task<Submission?> LockSubmissionAsync(AppDbContext db, Guid workspaceId, Guid submissionId,
        CancellationToken ct, Guid? mentorshipId = null) =>
        db.Submissions.FromSqlInterpolated(
                $"""SELECT * FROM "Submissions" WHERE "Id" = {submissionId} AND "WorkspaceId" = {workspaceId} FOR UPDATE""")
            .Where(s => mentorshipId == null || s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId == mentorshipId)
            .SingleOrDefaultAsync(ct);

    internal static async Task<ReviewDraftDto?> LoadAsync(AppDbContext db, Guid workspaceId, Guid submissionId, CancellationToken ct)
    {
        var draft = await db.ReviewDrafts.AsNoTracking().Include(d => d.CriterionScores)
            .SingleOrDefaultAsync(d => d.WorkspaceId == workspaceId && d.SubmissionId == submissionId, ct);
        return draft is null ? null : ToDto(draft);
    }

    internal static ReviewDraftDto ToDto(ReviewDraft draft) => new(draft.Id, draft.SubmissionId,
        new(draft.StrengthsMarkdown, draft.ImprovementsMarkdown, draft.ChallengeQuestionMarkdown, draft.Verdict,
            draft.CriterionScores.OrderBy(s => s.CriterionId)
                .Select(s => new CriterionScoreInput(s.CriterionId, s.Score, s.CommentMarkdown)).ToList()),
        draft.CreatedAtUtc, draft.UpdatedAtUtc, draft.Version);

    internal static AppError VersionConflict() =>
        Errors.Conflict("review_draft.version_conflict", "Szkic został zmieniony. Wczytaj jego aktualną wersję.");

    internal static async Task<Result<ReviewDraftDto>> SaveAsync(AppDbContext db, Submission submission,
        SaveReviewDraftRequest request, TimeProvider clock, CancellationToken ct)
    {
        if (submission.Status != SubmissionStatus.PendingReview)
            return Errors.Conflict("review.not_pending", "Ta próba nie wymaga oceny.");
        var draft = await db.ReviewDrafts.SingleOrDefaultAsync(
            d => d.WorkspaceId == submission.WorkspaceId && d.SubmissionId == submission.Id, ct);
        if (request.ExpectedVersion is <= 0 || (draft is null ? request.ExpectedVersion is not null :
                request.ExpectedVersion != draft.Version) || draft?.Version == long.MaxValue)
            return VersionConflict();

        var content = request.Content;
        if (content is null || content.Scores is null)
            return Errors.Validation("review_draft.invalid", "Podaj treść szkicu.");
        var exerciseId = await db.Assignments.AsNoTracking()
            .Where(a => a.Id == submission.AssignmentId && a.WorkspaceId == submission.WorkspaceId)
            .Select(a => a.ExerciseId).SingleAsync(ct);
        var criteria = await ReviewService.LoadReviewCriteriaAsync(db, submission.WorkspaceId, exerciseId, ct);
        var scoreIds = content.Scores.Select(s => s.CriterionId).Distinct().ToList();
        var known = await db.RubricCriteria.AsNoTracking()
            .Where(c => c.WorkspaceId == submission.WorkspaceId && scoreIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        if (scoreIds.Any(id => !known.Contains(id))) return Errors.NotFound("review");
        var errors = ReviewDraftValidator.Validate(content, criteria);
        if (errors.HasErrors) return errors.ToError("review_draft.invalid");

        var now = clock.GetUtcNow().UtcDateTime;
        if (draft is null)
        {
            draft = new ReviewDraft
            {
                Id = Guid.CreateVersion7(), WorkspaceId = submission.WorkspaceId, SubmissionId = submission.Id,
                CreatedAtUtc = now, Version = 1,
            };
            db.ReviewDrafts.Add(draft);
        }
        else
        {
            draft.Version++;
            await db.ReviewDraftCriterionScores.Where(s => s.WorkspaceId == submission.WorkspaceId && s.DraftId == draft.Id)
                .ExecuteDeleteAsync(ct);
        }
        draft.StrengthsMarkdown = content.StrengthsMarkdown?.Trim();
        draft.ImprovementsMarkdown = content.ImprovementsMarkdown?.Trim();
        draft.ChallengeQuestionMarkdown = content.ChallengeQuestionMarkdown?.Trim();
        draft.Verdict = content.Verdict;
        draft.UpdatedAtUtc = now;
        draft.CriterionScores = content.Scores.Select(s => new ReviewDraftCriterionScore
        {
            DraftId = draft.Id, WorkspaceId = submission.WorkspaceId, CriterionId = s.CriterionId,
            Score = (short)s.Score, CommentMarkdown = s.CommentMarkdown?.Trim() ?? "",
        }).ToList();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return VersionConflict(); }
        return ToDto(draft);
    }
}

internal sealed class McpReviewDraftService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    IOptionsMonitor<McpReviewOptions> options,
    TimeProvider clock) : IMcpReviewDraftService
{
    public async Task<Result<ReviewDraftDto?>> GetAsync(Guid actorUserId, Guid mentorshipId, Guid submissionId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is not { Role: AccessRole.WorkspaceOwner }) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await McpReviewAccess.LockAsync(db, acc, ct);
        if (mentorship is null || !await McpReviewAccess.Submissions(db, acc)
                .AnyAsync(s => s.Id == submissionId, ct)) return Errors.NotFound("review");
        var denied = await McpReviewAccess.CheckAsync(db, mentorship, options.CurrentValue, ct);
        if (denied is not null) return denied;
        return Result<ReviewDraftDto?>.Success(await ReviewDraftService.LoadAsync(db, acc.WorkspaceId, submissionId, ct));
    }

    public async Task<Result<ReviewDraftDto>> SaveAsync(Guid actorUserId, Guid mentorshipId, SaveReviewDraftRequest request, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is not { Role: AccessRole.WorkspaceOwner }) return Errors.NotFound("review");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await McpReviewAccess.LockAsync(db, acc, ct);
        if (mentorship is null) return Errors.NotFound("review");
        var submission = await ReviewDraftService.LockSubmissionAsync(db, acc.WorkspaceId, request.SubmissionId, ct, mentorshipId);
        if (submission is null) return Errors.NotFound("review");
        var denied = await McpReviewAccess.CheckAsync(db, mentorship, options.CurrentValue, ct);
        if (denied is not null) return denied;
        var result = await ReviewDraftService.SaveAsync(db, submission, request, clock, ct);
        if (result.IsSuccess) await tx.CommitAsync(ct);
        return result;
    }
}

internal static class McpReviewAccess
{
    internal static Task<Mentorship?> LockAsync(AppDbContext db, MentorshipAccess acc, CancellationToken ct) =>
        db.Mentorships.FromSqlInterpolated(
                $"""SELECT * FROM "Mentorships" WHERE "Id" = {acc.MentorshipId} AND "WorkspaceId" = {acc.WorkspaceId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);

    internal static IQueryable<Submission> Submissions(AppDbContext db, MentorshipAccess acc) =>
        db.Submissions.AsNoTracking().Where(s => s.WorkspaceId == acc.WorkspaceId &&
            s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId == acc.MentorshipId);

    internal static async Task<AppError?> CheckAsync(AppDbContext db, Mentorship mentorship, McpReviewOptions options, CancellationToken ct)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ProviderNotice))
            return new(ErrorKind.Unavailable, "mcp_review.disabled", "Review przez MCP jest niedostępne.");
        if (mentorship.Status == MentorshipStatus.Closed)
            return Errors.Conflict("mcp_review.mentorship_closed", "Mentoring jest zamknięty.");
        if (!await db.MentorshipReviewConsents.AsNoTracking().AnyAsync(c => c.WorkspaceId == mentorship.WorkspaceId &&
                c.MentorshipId == mentorship.Id && c.RevokedAtUtc == null && c.DisclosureVersion == options.DisclosureVersion, ct))
            return Errors.Conflict("mcp_review.consent_required", "Brak aktualnej zgody na review przez MCP.");
        return null;
    }
}
