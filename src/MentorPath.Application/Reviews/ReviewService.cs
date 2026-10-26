using MentorPath.Application.Catalog;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Reviews;

internal sealed class ReviewService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IReviewService
{
    private const string Module = "review";

    public async Task<Result<IReadOnlyList<PendingReviewItem>>> GetPendingAsync(Guid actorUserId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await QueryPendingAsync(db, ws.Value, ct);
    }

    internal static async Task<List<PendingReviewItem>> QueryPendingAsync(AppDbContext db, Guid workspaceId, CancellationToken ct) =>
        await db.Submissions.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId && s.Status == SubmissionStatus.PendingReview)
            .OrderBy(s => s.SubmittedAtUtc)
            .Select(s => new PendingReviewItem(s.Id, s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId, s.Mentee!.DisplayName,
                s.Assignment.Exercise!.Title, s.Assignment.Exercise.Type, s.AttemptNumber, s.SubmittedAtUtc, s.Assignment.DueAtUtc))
            .ToListAsync(ct);

    public async Task<Result<ReviewDetailDto>> GetReviewDetailAsync(Guid actorUserId, Guid submissionId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.Submissions.AsNoTracking()
            .Where(x => x.Id == submissionId && x.WorkspaceId == ws)
            .Select(x => new
            {
                x.Id, x.Status, x.AssignmentId, x.MenteeUserId,
                MenteeName = x.Mentee!.DisplayName,
                x.Assignment!.ExerciseId,
                x.Assignment!.IsActive,
                ItemActive = x.Assignment!.RoadmapItem!.IsActive,
                Roadmap = x.Assignment!.RoadmapItem!.Roadmap!,
                MentorshipStatus = x.Assignment!.RoadmapItem!.Roadmap!.Mentorship!.Status,
            })
            .FirstOrDefaultAsync(ct);
        if (s is null) return Errors.NotFound(Module);

        if (s.Status == SubmissionStatus.PendingReview)
        {
            db.ActivityEvents.Add(new ActivityEvent
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = ws.Value,
                AssignmentId = s.AssignmentId,
                SubmissionId = s.Id,
                MenteeUserId = s.MenteeUserId,
                MentorUserId = actorUserId,
                EventType = ActivityEventType.ReviewOpened,
                OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, "IX_ActivityEvents_WorkspaceId_SubmissionId_EventType"))
            {
                // Concurrent review page loads produce one first-open event.
            }
        }

        var exercise = await db.Exercises.AsNoTracking().Include(e => e.QuizOptions).Include(e => e.Topic)
            .FirstAsync(e => e.Id == s.ExerciseId && e.WorkspaceId == ws, ct);
        var acc = new AssignmentAccess(s.AssignmentId, ws.Value, s.Roadmap.MentorshipId, s.MenteeUserId, s.ExerciseId,
            AccessRole.WorkspaceOwner, s.MentorshipStatus, s.Roadmap.Status, s.IsActive, s.ItemActive);
        var history = await SubmissionService.LoadHistoryAsync(db, acc, ct);
        var criteria = (await LoadReviewCriteriaAsync(db, ws.Value, s.ExerciseId, ct)).Select(RubricService.ToDto).ToList();
        var criterionIds = await ExerciseAuthoringService.LoadCriterionIdsAsync(db, ws.Value, s.ExerciseId, activeOnly: false, ct);
        return new ReviewDetailDto(s.Id, s.Roadmap.MentorshipId, s.MenteeName, s.Status,
            ExerciseAuthoringService.ToMentorDto(exercise, criterionIds), history, criteria);
    }

    // Criteria a review must score: those selected for the exercise that are still active.
    private static Task<List<RubricCriterion>> LoadReviewCriteriaAsync(AppDbContext db, Guid workspaceId, Guid exerciseId, CancellationToken ct) =>
        db.ExerciseCriteria.AsNoTracking()
            .Where(x => x.ExerciseId == exerciseId && x.WorkspaceId == workspaceId && x.Criterion!.IsActive)
            .Select(x => x.Criterion!)
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Result<Guid>> SubmitFeedbackAsync(Guid actorUserId, SubmitFeedbackRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);

        var errors = new FieldErrors();
        errors.Required(nameof(request.StrengthsMarkdown), request.StrengthsMarkdown, InputLimits.Markdown, "Mocne strony");
        errors.Required(nameof(request.ImprovementsMarkdown), request.ImprovementsMarkdown, InputLimits.Markdown, "Do poprawy");
        errors.Required(nameof(request.ChallengeQuestionMarkdown), request.ChallengeQuestionMarkdown, InputLimits.Markdown, "Pytanie pogłębiające");
        if (!Enum.IsDefined(request.Verdict)) errors.Add(nameof(request.Verdict), "Wybierz werdykt.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.AsNoTracking()
            .Where(s => s.Id == request.SubmissionId && s.WorkspaceId == ws)
            .Select(s => new { s.AssignmentId, s.MenteeUserId, s.Assignment!.ExerciseId })
            .FirstOrDefaultAsync(ct);
        if (submission is null) return Errors.NotFound(Module);

        var required = await LoadReviewCriteriaAsync(db, ws.Value, submission.ExerciseId, ct);
        var scores = request.Scores ?? [];
        var workspaceCriterionIds = await db.RubricCriteria.AsNoTracking().Where(c => c.WorkspaceId == ws).Select(c => c.Id).ToListAsync(ct);
        if (scores.Any(x => !workspaceCriterionIds.Contains(x.CriterionId))) return Errors.NotFound(Module);
        ValidateScores(errors, scores, required);
        if (errors.HasErrors) return errors.ToError("review.invalid");

        var newStatus = request.Verdict == ReviewVerdict.Accepted ? SubmissionStatus.Accepted : SubmissionStatus.NeedsRevision;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Submissions
            .Where(s => s.Id == request.SubmissionId && s.WorkspaceId == ws && s.Status == SubmissionStatus.PendingReview)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, newStatus), ct);
        if (rows != 1)
        {
            await tx.RollbackAsync(CancellationToken.None);
            var exists = await db.Submissions.AnyAsync(s => s.Id == request.SubmissionId && s.WorkspaceId == ws, ct);
            return exists
                ? Errors.Conflict("review.not_pending", "Ta próba została już oceniona lub nie wymaga oceny.")
                : Errors.NotFound(Module);
        }

        var feedback = new MentorFeedback
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            SubmissionId = request.SubmissionId,
            MentorUserId = actorUserId,
            Verdict = request.Verdict,
            StrengthsMarkdown = request.StrengthsMarkdown.Trim(),
            ImprovementsMarkdown = request.ImprovementsMarkdown.Trim(),
            ChallengeQuestionMarkdown = request.ChallengeQuestionMarkdown.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        var openedAt = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == ws && e.SubmissionId == request.SubmissionId && e.EventType == ActivityEventType.ReviewOpened)
            .Select(e => (DateTime?)e.OccurredAtUtc)
            .SingleOrDefaultAsync(ct);
        if (openedAt is null)
        {
            openedAt = feedback.CreatedAtUtc;
            db.ActivityEvents.Add(new ActivityEvent
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = ws.Value,
                AssignmentId = submission.AssignmentId,
                SubmissionId = request.SubmissionId,
                MenteeUserId = submission.MenteeUserId,
                MentorUserId = actorUserId,
                EventType = ActivityEventType.ReviewOpened,
                OccurredAtUtc = openedAt.Value,
            });
        }
        var reviewDuration = feedback.CreatedAtUtc - openedAt.Value;
        if (reviewDuration < TimeSpan.Zero)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return Errors.Conflict("review.clock_error", "Nie można zapisać pomiaru czasu review. Spróbuj ponownie.");
        }
        db.ActivityEvents.Add(new ActivityEvent
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            AssignmentId = submission.AssignmentId,
            SubmissionId = request.SubmissionId,
            MenteeUserId = submission.MenteeUserId,
            MentorUserId = actorUserId,
            EventType = ActivityEventType.ReviewSubmitted,
            OccurredAtUtc = feedback.CreatedAtUtc,
            ReviewDuration = reviewDuration,
            ExceedsTwoHours = reviewDuration > TimeSpan.FromHours(2),
        });
        db.MentorFeedback.Add(feedback);
        foreach (var score in scores)
            db.FeedbackCriterionScores.Add(new FeedbackCriterionScore
            {
                WorkspaceId = ws.Value,
                FeedbackId = feedback.Id,
                CriterionId = score.CriterionId,
                Score = (short)score.Score,
                CommentMarkdown = score.CommentMarkdown?.Trim() ?? "",
            });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (DbErrors.ConstraintName(ex) == "IX_ActivityEvents_WorkspaceId_SubmissionId_EventType")
                return Errors.Conflict("review.open_event_race", "Nie udało się zapisać początku pomiaru review. Spróbuj ponownie.");
            return Errors.Conflict("review.already_reviewed", "Ta próba ma już feedback.");
        }
        return feedback.Id;
    }

    private static void ValidateScores(FieldErrors errors, IReadOnlyList<CriterionScoreInput> scores, IReadOnlyList<RubricCriterion> required)
    {
        const string Field = nameof(SubmitFeedbackRequest.Scores);
        var requiredIds = required.Select(c => c.Id).ToHashSet();
        if (scores.Select(x => x.CriterionId).Distinct().Count() != scores.Count)
            errors.Add(Field, "Każde kryterium może mieć tylko jedną ocenę.");
        if (scores.Any(x => !requiredIds.Contains(x.CriterionId)))
            errors.Add(Field, "Oceniono kryterium, które nie dotyczy tego ćwiczenia.");
        foreach (var c in required.Where(c => scores.All(x => x.CriterionId != c.Id)))
            errors.Add(Field, $"Brak oceny dla kryterium „{c.Name}”.");
        for (var i = 0; i < scores.Count; i++)
        {
            errors.Range($"{Field}[{i}].Score", scores[i].Score, 1, 5, "Ocena");
            errors.Optional($"{Field}[{i}].CommentMarkdown", scores[i].CommentMarkdown, InputLimits.Markdown, "Komentarz do kryterium");
        }
    }
}

internal sealed class ReflectionService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IReflectionService
{
    private const string Module = "reflection";

    public async Task<Result> SaveReflectionAsync(Guid actorUserId, SaveReflectionRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        errors.Confidence(nameof(request.ConfidenceAfter), request.ConfidenceAfter);
        errors.Required(nameof(request.TakeawayMarkdown), request.TakeawayMarkdown, InputLimits.Markdown, "Najważniejszy wniosek");
        if (errors.HasErrors) return errors.ToError("reflection.invalid");

        if (!await access.CanAccessSubmissionAsync(actorUserId, request.SubmissionId, ct)) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var submission = await db.Submissions.AsNoTracking()
            .Where(s => s.Id == request.SubmissionId && s.MenteeUserId == actorUserId)
            .Select(s => new { s.Id, s.Status, HasReflection = s.Reflection != null })
            .FirstOrDefaultAsync(ct);
        if (submission is null) return Errors.NotFound(Module);
        if (!SubmissionService.CanReflectOn(submission.Status))
            return Errors.Conflict("reflection.not_reviewed", "Refleksję można dodać dopiero po ocenie próby.");
        if (submission.HasReflection) return AlreadyExists();

        db.SubmissionReflections.Add(new SubmissionReflection
        {
            Id = Guid.CreateVersion7(),
            SubmissionId = submission.Id,
            ConfidenceAfter = (short)request.ConfidenceAfter,
            TakeawayMarkdown = request.TakeawayMarkdown.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return AlreadyExists();
        }
        return Result.Success();
    }

    private static AppError AlreadyExists() => Errors.Conflict("reflection.already_exists", "Refleksja dla tej próby już istnieje.");
}
