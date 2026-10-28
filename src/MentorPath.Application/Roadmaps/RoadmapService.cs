using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Roadmaps;

internal sealed class RoadmapService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IRoadmapService
{
    private const string Module = "roadmap";

    public async Task<Result<Guid>> CreateAsync(Guid actorUserId, CreateRoadmapRequest request, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, request.MentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        var errors = ValidateDetails(request.Title, request.ObjectiveMarkdown);
        if (errors.HasErrors) return errors.ToError("roadmap.invalid");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Roadmaps.AnyAsync(r => r.MentorshipId == request.MentorshipId, ct))
            return Errors.Conflict("roadmap.already_exists", "Ta relacja mentoringowa ma już roadmapę.");
        var roadmap = new Roadmap
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = acc.WorkspaceId,
            MentorshipId = acc.MentorshipId,
            Title = request.Title.Trim(),
            ObjectiveMarkdown = request.ObjectiveMarkdown.Trim(),
            Status = RoadmapStatus.Draft,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Roadmaps.Add(roadmap);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Errors.Conflict("roadmap.already_exists", "Ta relacja mentoringowa ma już roadmapę.");
        }
        return roadmap.Id;
    }

    public async Task<Result<MentorRoadmapDto?>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.AsNoTracking()
            .Where(r => r.MentorshipId == mentorshipId && r.WorkspaceId == acc.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        if (roadmap is null) return Result<MentorRoadmapDto?>.Success(null);

        var items = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.RoadmapId == roadmap.Id && i.WorkspaceId == acc.WorkspaceId)
            .OrderBy(i => i.Position)
            .Select(i => new MentorRoadmapItemDto(i.Id, i.TopicId, i.Topic!.Title, i.Position, i.IsActive, i.MentorNoteMarkdown,
                i.Assignments.Any(a => a.Submissions.Any()),
                i.Assignments.OrderBy(a => a.Position).Select(a => new MentorAssignmentDto(a.Id, a.ExerciseId, a.Exercise!.Title, a.Exercise.Type,
                    a.Position, a.IsRequired, a.IsActive, a.DueAtUtc, a.Submissions.Count)).ToList()))
            .ToListAsync(ct);
        return new MentorRoadmapDto(roadmap.Id, roadmap.MentorshipId, roadmap.Title, roadmap.ObjectiveMarkdown, roadmap.Status,
            roadmap.PublishedAtUtc, items);
    }

    // The feature PR replaces this compile-only placeholder.
    public Task<Result> SetCompetencyStatusAsync(Guid actorUserId, Guid roadmapItemId, SetCompetencyStatusRequest request, CancellationToken ct) =>
        Task.FromResult<Result>(Errors.NotFound(Module));

    public async Task<Result> UpdateDetailsAsync(Guid actorUserId, Guid roadmapId, UpdateRoadmapRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = ValidateDetails(request.Title, request.ObjectiveMarkdown);
        if (errors.HasErrors) return errors.ToError("roadmap.invalid");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Roadmaps.Where(r => r.Id == roadmapId && r.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Title, request.Title.Trim()).SetProperty(r => r.ObjectiveMarkdown, request.ObjectiveMarkdown.Trim()), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    public async Task<Result<Guid>> AddTopicAsync(Guid actorUserId, AddRoadmapTopicRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = new FieldErrors();
        errors.Optional(nameof(request.MentorNoteMarkdown), request.MentorNoteMarkdown, InputLimits.Markdown, "Notatka");
        if (errors.HasErrors) return errors.ToError("roadmap.invalid");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RoadmapId && r.WorkspaceId == ws, ct);
        if (roadmap is null) return Errors.NotFound(Module);
        var topic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TopicId && t.WorkspaceId == ws, ct);
        if (topic is null) return Errors.NotFound(Module);
        if (topic.IsArchived) return Errors.Validation("roadmap.topic_archived", "Nie można dodać zarchiwizowanego tematu.");
        if (await db.RoadmapItems.AnyAsync(i => i.RoadmapId == roadmap.Id && i.TopicId == topic.Id, ct))
            return Errors.Conflict("roadmap.topic_exists", "Ten temat jest już na roadmapie.");

        var position = await db.RoadmapItems.Where(i => i.RoadmapId == roadmap.Id).CountAsync(ct);
        var item = new RoadmapItem
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            RoadmapId = roadmap.Id,
            TopicId = topic.Id,
            Position = position,
            MentorNoteMarkdown = Text.NullIfBlank(request.MentorNoteMarkdown),
            IsActive = true,
        };
        db.RoadmapItems.Add(item);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Errors.Conflict("roadmap.topic_exists", "Ten temat jest już na roadmapie.");
        }
        return item.Id;
    }

    public async Task<Result> UpdateTopicNoteAsync(Guid actorUserId, Guid roadmapItemId, string? mentorNoteMarkdown, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = new FieldErrors();
        errors.Optional("MentorNoteMarkdown", mentorNoteMarkdown, InputLimits.Markdown, "Notatka");
        if (errors.HasErrors) return errors.ToError("roadmap.invalid");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var note = Text.NullIfBlank(mentorNoteMarkdown);
        var rows = await db.RoadmapItems.Where(i => i.Id == roadmapItemId && i.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.MentorNoteMarkdown, note), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    public async Task<Result> ReorderTopicsAsync(Guid actorUserId, Guid roadmapId, IReadOnlyList<Guid> orderedRoadmapItemIds, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.RoadmapItems.Where(i => i.RoadmapId == roadmapId && i.WorkspaceId == ws).ToListAsync(ct);
        if (items.Count == 0) return Errors.NotFound(Module);
        if (!SameSet(items.Select(i => i.Id), orderedRoadmapItemIds))
            return Errors.Validation("roadmap.invalid_order", "Lista kolejności musi zawierać wszystkie tematy roadmapy.");
        foreach (var item in items) item.Position = IndexOf(orderedRoadmapItemIds, item.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetTopicActiveAsync(Guid actorUserId, Guid roadmapItemId, bool isActive, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.RoadmapItems.Where(i => i.Id == roadmapItemId && i.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsActive, isActive), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    public async Task<Result> RemoveTopicAsync(Guid actorUserId, Guid roadmapItemId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.RoadmapItems.Include(i => i.Assignments)
            .FirstOrDefaultAsync(i => i.Id == roadmapItemId && i.WorkspaceId == ws, ct);
        if (item is null) return Errors.NotFound(Module);
        if (await db.Submissions.AnyAsync(s => s.WorkspaceId == ws && s.Assignment!.RoadmapItemId == item.Id, ct))
            return HasHistory();
        db.Assignments.RemoveRange(item.Assignments);
        db.RoadmapItems.Remove(item);
        await db.SaveChangesAsync(ct);
        await RenumberItemsAsync(db, item.RoadmapId, ct);
        return Result.Success();
    }

    public async Task<Result<Guid>> AssignExerciseAsync(Guid actorUserId, AssignExerciseRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.RoadmapItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.RoadmapItemId && i.WorkspaceId == ws, ct);
        if (item is null) return Errors.NotFound(Module);
        var exercise = await db.Exercises.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.ExerciseId && e.WorkspaceId == ws, ct);
        if (exercise is null) return Errors.NotFound(Module);
        if (exercise.Status != ExerciseStatus.Published)
            return Errors.Validation("roadmap.exercise_not_published", "Można przypisać tylko opublikowane ćwiczenie.");
        if (exercise.TopicId != item.TopicId)
            return Errors.Validation("roadmap.exercise_topic_mismatch", "Ćwiczenie musi należeć do tematu z roadmapy.");
        if (await db.Assignments.AnyAsync(a => a.RoadmapItemId == item.Id && a.ExerciseId == exercise.Id, ct))
            return Errors.Conflict("roadmap.assignment_exists", "To ćwiczenie jest już przypisane do tego tematu.");

        var position = await db.Assignments.CountAsync(a => a.RoadmapItemId == item.Id, ct);
        var assignment = new Assignment
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            RoadmapItemId = item.Id,
            ExerciseId = exercise.Id,
            Position = position,
            IsRequired = request.IsRequired,
            IsActive = true,
            DueAtUtc = ToUtc(request.DueAtUtc),
        };
        db.Assignments.Add(assignment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Errors.Conflict("roadmap.assignment_exists", "To ćwiczenie jest już przypisane do tego tematu.");
        }
        return assignment.Id;
    }

    public async Task<Result> UpdateAssignmentAsync(Guid actorUserId, Guid assignmentId, UpdateAssignmentRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = ToUtc(request.DueAtUtc);
        var rows = await db.Assignments.Where(a => a.Id == assignmentId && a.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsRequired, request.IsRequired).SetProperty(a => a.DueAtUtc, due), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    public async Task<Result> ReorderAssignmentsAsync(Guid actorUserId, Guid roadmapItemId, IReadOnlyList<Guid> orderedAssignmentIds, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.Assignments.Where(a => a.RoadmapItemId == roadmapItemId && a.WorkspaceId == ws).ToListAsync(ct);
        if (items.Count == 0) return Errors.NotFound(Module);
        if (!SameSet(items.Select(a => a.Id), orderedAssignmentIds))
            return Errors.Validation("roadmap.invalid_order", "Lista kolejności musi zawierać wszystkie przypisania tematu.");
        foreach (var a in items) a.Position = IndexOf(orderedAssignmentIds, a.Id);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetAssignmentActiveAsync(Guid actorUserId, Guid assignmentId, bool isActive, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Assignments.Where(a => a.Id == assignmentId && a.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, isActive), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    public async Task<Result> RemoveAssignmentAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.WorkspaceId == ws, ct);
        if (assignment is null) return Errors.NotFound(Module);
        if (await db.Submissions.AnyAsync(s => s.AssignmentId == assignment.Id, ct)) return HasHistory();
        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
        var rest = await db.Assignments.Where(a => a.RoadmapItemId == assignment.RoadmapItemId).OrderBy(a => a.Position).ToListAsync(ct);
        for (var i = 0; i < rest.Count; i++) rest[i].Position = i;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> PublishAsync(Guid actorUserId, Guid roadmapId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.FirstOrDefaultAsync(r => r.Id == roadmapId && r.WorkspaceId == ws, ct);
        if (roadmap is null) return Errors.NotFound(Module);
        if (roadmap.Status == RoadmapStatus.Published) return Errors.Conflict("roadmap.already_published", "Roadmapa jest już opublikowana.");

        var hasRequired = await db.RoadmapItems.AnyAsync(i => i.RoadmapId == roadmap.Id && i.IsActive
            && i.Assignments.Any(a => a.IsActive && a.IsRequired), ct);
        if (!hasRequired)
            return Errors.Validation("roadmap.publish_invalid", "Roadmapa musi mieć co najmniej jeden aktywny temat z wymaganym ćwiczeniem.");

        roadmap.Status = RoadmapStatus.Published;
        roadmap.PublishedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<MenteeRoadmapDto>> GetForMenteeAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.AsNoTracking()
            .FirstOrDefaultAsync(r => r.MentorshipId == mentorshipId && r.WorkspaceId == acc.WorkspaceId && r.Status == RoadmapStatus.Published, ct);
        if (roadmap is null) return Errors.NotFound(Module);

        var items = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.RoadmapId == roadmap.Id && i.WorkspaceId == acc.WorkspaceId && i.IsActive)
            .OrderBy(i => i.Position)
            .Select(i => new MenteeRoadmapItemDto(i.Id, i.Topic!.Title, i.Topic.DescriptionMarkdown, i.MentorNoteMarkdown, i.Position,
                i.Assignments.Where(a => a.IsActive).OrderBy(a => a.Position)
                    .Select(a => new MenteeAssignmentListItem(a.Id, a.Exercise!.Title, a.Exercise.Type, a.IsRequired, a.DueAtUtc,
                        a.Submissions.OrderByDescending(s => s.AttemptNumber).Select(s => (SubmissionStatus?)s.Status).FirstOrDefault(),
                        a.Submissions.Count))
                    .ToList()))
            .ToListAsync(ct);
        return new MenteeRoadmapDto(roadmap.Id, roadmap.MentorshipId, roadmap.Title, roadmap.ObjectiveMarkdown, items);
    }

    private static FieldErrors ValidateDetails(string title, string objective)
    {
        var e = new FieldErrors();
        e.Required("Title", title, InputLimits.Title, "Tytuł");
        e.Required("ObjectiveMarkdown", objective, InputLimits.Markdown, "Cel roadmapy");
        return e;
    }

    private static async Task RenumberItemsAsync(AppDbContext db, Guid roadmapId, CancellationToken ct)
    {
        var rest = await db.RoadmapItems.Where(i => i.RoadmapId == roadmapId).OrderBy(i => i.Position).ToListAsync(ct);
        for (var i = 0; i < rest.Count; i++) rest[i].Position = i;
        await db.SaveChangesAsync(ct);
    }

    private static bool SameSet(IEnumerable<Guid> existing, IReadOnlyList<Guid> ordered) =>
        ordered.Count == ordered.Distinct().Count() && existing.ToHashSet().SetEquals(ordered);

    private static int IndexOf(IReadOnlyList<Guid> list, Guid id)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == id) return i;
        return -1;
    }

    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

    private static AppError HasHistory() => Errors.Conflict("roadmap.has_submissions",
        "Element ma historyczne rozwiązania i nie może zostać usunięty. Oznacz go jako nieaktywny.");
}
