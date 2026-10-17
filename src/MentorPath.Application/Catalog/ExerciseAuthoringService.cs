using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Catalog;

internal sealed class ExerciseAuthoringService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IExerciseAuthoringService
{
    private const string Module = "catalog";

    public Task<Result<Guid>> CreateDraftAsync(Guid actorUserId, CreateExerciseDraftRequest request, CancellationToken ct) =>
        CreateDraftAsync(actorUserId, request, ExerciseSource.Manual, ct);

    internal async Task<Result<Guid>> CreateDraftAsync(Guid actorUserId, CreateExerciseDraftRequest request, ExerciseSource source, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = ExerciseValidator.ValidateDraft(request.Type, request.Content);
        if (errors.HasErrors) return errors.ToError("catalog.invalid_exercise");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TopicId && t.WorkspaceId == ws, ct);
        if (topic is null) return Errors.NotFound(Module);
        if (topic.IsArchived) return Errors.Validation("catalog.topic_archived", "Nie można dodawać ćwiczeń do zarchiwizowanego tematu.");

        var exercise = new Exercise
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            TopicId = topic.Id,
            Type = request.Type,
            Status = ExerciseStatus.Draft,
            Source = source,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        ApplyContent(exercise, request.Content);
        db.Exercises.Add(exercise);
        await db.SaveChangesAsync(ct);
        return exercise.Id;
    }

    public async Task<Result> UpdateDraftAsync(Guid actorUserId, Guid exerciseId, ExerciseContent content, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.Include(e => e.QuizOptions)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.WorkspaceId == ws, ct);
        if (exercise is null) return Errors.NotFound(Module);
        if (exercise.Status != ExerciseStatus.Draft) return NotDraft();

        var errors = ExerciseValidator.ValidateDraft(exercise.Type, content);
        if (errors.HasErrors) return errors.ToError("catalog.invalid_exercise");

        db.QuizOptions.RemoveRange(exercise.QuizOptions);
        exercise.QuizOptions.Clear();
        // Remove old options first so the single-correct partial unique index is never violated mid-update.
        await db.SaveChangesAsync(ct);
        ApplyContent(exercise, content);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> PublishAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.AsNoTracking().Include(e => e.QuizOptions)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.WorkspaceId == ws, ct);
        if (exercise is null) return Errors.NotFound(Module);
        if (exercise.Status != ExerciseStatus.Draft) return NotDraft();

        var errors = ExerciseValidator.ValidateForPublish(exercise.Type, ToContent(exercise));
        if (errors.HasErrors) return Errors.Validation("catalog.publish_invalid", "Ćwiczenie nie spełnia wymagań publikacji.", errors.ToDictionary());

        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Exercises
            .Where(e => e.Id == exerciseId && e.WorkspaceId == ws && e.Status == ExerciseStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ExerciseStatus.Published).SetProperty(e => e.PublishedAtUtc, now), ct);
        return rows == 1 ? Result.Success() : NotDraft();
    }

    public async Task<Result<Guid>> CloneAsDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var source = await db.Exercises.AsNoTracking().Include(e => e.QuizOptions)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.WorkspaceId == ws, ct);
        if (source is null) return Errors.NotFound(Module);

        var clone = new Exercise
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            TopicId = source.TopicId,
            Type = source.Type,
            Status = ExerciseStatus.Draft,
            Source = source.Source,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        var content = ToContent(source);
        ApplyContent(clone, content with { Title = Truncate($"{source.Title} (kopia)", InputLimits.Title) });
        db.Exercises.Add(clone);
        await db.SaveChangesAsync(ct);
        return clone.Id;
    }

    public async Task<Result> ArchiveAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var status = await db.Exercises.Where(e => e.Id == exerciseId && e.WorkspaceId == ws).Select(e => (ExerciseStatus?)e.Status).FirstOrDefaultAsync(ct);
        if (status is null) return Errors.NotFound(Module);
        if (status != ExerciseStatus.Published)
            return Errors.Conflict("catalog.exercise_not_published", "Archiwizować można tylko opublikowane ćwiczenia. Szkic możesz usunąć.");
        await db.Exercises.Where(e => e.Id == exerciseId && e.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ExerciseStatus.Archived), ct);
        return Result.Success();
    }

    public async Task<Result> DeleteDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exercise = await db.Exercises.Include(e => e.QuizOptions)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.WorkspaceId == ws, ct);
        if (exercise is null) return Errors.NotFound(Module);
        if (exercise.Status != ExerciseStatus.Draft) return NotDraft();
        db.Exercises.Remove(exercise);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<MentorExerciseDto>> GetMentorExerciseAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.Exercises.AsNoTracking().Include(x => x.QuizOptions).Include(x => x.Topic)
            .FirstOrDefaultAsync(x => x.Id == exerciseId && x.WorkspaceId == ws, ct);
        if (e is null) return Errors.NotFound(Module);
        return ToMentorDto(e);
    }

    internal static MentorExerciseDto ToMentorDto(Exercise e) =>
        new(e.Id, e.TopicId, e.Topic?.Title ?? "", e.Type, e.Status, e.Source, ToContent(e), e.PublishedAtUtc, e.CreatedAtUtc);

    public async Task<Result<IReadOnlyList<ExerciseListItem>>> ListAsync(Guid actorUserId, ExerciseListFilter filter, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Exercises.AsNoTracking().Where(e => e.WorkspaceId == ws);
        if (filter.TopicId is { } topicId) q = q.Where(e => e.TopicId == topicId);
        if (filter.Status is { } status) q = q.Where(e => e.Status == status);
        if (filter.Type is { } type) q = q.Where(e => e.Type == type);
        var items = await q.OrderByDescending(e => e.CreatedAtUtc)
            .Select(e => new ExerciseListItem(e.Id, e.TopicId, e.Title, e.Type, e.Status, e.Source, e.CreatedAtUtc))
            .ToListAsync(ct);
        return items;
    }

    public async Task<Result<MenteeExerciseDto>> GetMenteeExerciseAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct)
    {
        var acc = await access.GetAssignmentAccessAsync(actorUserId, assignmentId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.Exercises.AsNoTracking().Include(x => x.QuizOptions)
            .FirstOrDefaultAsync(x => x.Id == acc.ExerciseId && x.WorkspaceId == acc.WorkspaceId, ct);
        if (e is null) return Errors.NotFound(Module);
        return ToMenteeDto(e);
    }

    internal static MenteeExerciseDto ToMenteeDto(Exercise e) => new(
        e.Id, e.Type, e.Title, e.ScenarioMarkdown,
        Text.NullIfBlank(e.InstructionsMarkdown), e.ExpectedOutcomeMarkdown, Text.NullIfBlank(e.RubricMarkdown),
        e.DiscussionQuestionsMarkdown, e.EstimatedMinutes,
        e.QuizOptions.OrderBy(o => o.Position).Select(o => new MenteeQuizOptionDto(o.Id, o.Text, o.Position)).ToList());

    internal static ExerciseContent ToContent(Exercise e) => new(
        e.Title, e.ScenarioMarkdown, Text.NullIfBlank(e.InstructionsMarkdown), e.ExpectedOutcomeMarkdown,
        Text.NullIfBlank(e.RubricMarkdown), e.DiscussionQuestionsMarkdown, e.ExplanationMarkdown,
        Text.NullIfBlank(e.MentorGuideMarkdown), e.EstimatedMinutes,
        e.QuizOptions.OrderBy(o => o.Position).Select(o => new QuizOptionInput(o.Text, o.IsCorrect, Text.NullIfBlank(o.ExplanationMarkdown))).ToList());

    private static void ApplyContent(Exercise e, ExerciseContent c)
    {
        e.Title = c.Title.Trim();
        e.ScenarioMarkdown = c.ScenarioMarkdown?.Trim() ?? "";
        e.InstructionsMarkdown = c.InstructionsMarkdown?.Trim() ?? "";
        e.ExpectedOutcomeMarkdown = Text.NullIfBlank(c.ExpectedOutcomeMarkdown);
        e.RubricMarkdown = c.RubricMarkdown?.Trim() ?? "";
        e.DiscussionQuestionsMarkdown = Text.NullIfBlank(c.DiscussionQuestionsMarkdown);
        e.MentorGuideMarkdown = c.MentorGuideMarkdown?.Trim() ?? "";
        e.ExplanationMarkdown = Text.NullIfBlank(c.ExplanationMarkdown);
        e.EstimatedMinutes = c.EstimatedMinutes;
        e.QuizOptions = e.Type == ExerciseType.QuizSingleChoice
            ? (c.QuizOptions ?? []).Select((o, i) => new QuizOption
            {
                Id = Guid.CreateVersion7(),
                ExerciseId = e.Id,
                Text = o.Text?.Trim() ?? "",
                IsCorrect = o.IsCorrect,
                ExplanationMarkdown = o.ExplanationMarkdown?.Trim() ?? "",
                Position = i,
            }).ToList()
            : [];
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static AppError NotDraft() =>
        Errors.Conflict("catalog.exercise_not_draft", "Ćwiczenie nie jest szkicem — opublikowanej treści nie można zmieniać. Utwórz kopię jako nowy szkic.");
}
