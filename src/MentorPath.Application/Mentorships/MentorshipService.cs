using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Mentorships;

internal sealed class MentorshipService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IMentorshipService
{
    private const string Module = "mentorship";

    public async Task<Result<IReadOnlyList<MenteeMentorshipListItem>>> ListForMenteeAsync(Guid actorUserId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.Mentorships.AsNoTracking()
            .Where(m => m.MenteeUserId == actorUserId && m.Status != MentorshipStatus.Closed)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MenteeMentorshipListItem(m.Id, m.Workspace!.Name, m.Workspace.Owner!.DisplayName, m.Status, m.Goal != null))
            .ToListAsync(ct);
        return items;
    }

    public async Task<Result<OnboardingDto>> GetOnboardingAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Mentorships.AsNoTracking()
            .Where(m => m.Id == mentorshipId)
            .Select(m => new OnboardingDto(m.Id, m.Goal, m.CurrentLevel, m.Challenges, m.WeeklyHours))
            .FirstAsync(ct);
    }

    public async Task<Result> SaveOnboardingAsync(Guid actorUserId, Guid mentorshipId, SaveOnboardingRequest request, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        if (!acc.MenteeCanWrite) return Errors.Conflict("mentorship.not_active", "Mentoring nie jest aktywny.");

        var errors = new FieldErrors();
        errors.Required(nameof(request.Goal), request.Goal, InputLimits.Markdown, "Cel mentoringu");
        errors.Required(nameof(request.Challenges), request.Challenges, InputLimits.Markdown, "Największe wyzwania");
        errors.Range(nameof(request.WeeklyHours), request.WeeklyHours, 1, 80, "Godziny tygodniowo");
        if (!Enum.IsDefined(request.CurrentLevel)) errors.Add(nameof(request.CurrentLevel), "Wybierz poziom doświadczenia.");
        if (errors.HasErrors) return errors.ToError("mentorship.invalid_onboarding");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Mentorships.Where(m => m.Id == mentorshipId && m.WorkspaceId == acc.WorkspaceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Goal, request.Goal.Trim())
                .SetProperty(m => m.CurrentLevel, request.CurrentLevel)
                .SetProperty(m => m.Challenges, request.Challenges.Trim())
                .SetProperty(m => m.WeeklyHours, request.WeeklyHours)
                .SetProperty(m => m.UpdatedAtUtc, now), ct);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<MentorMenteeListItem>>> ListForMentorAsync(Guid actorUserId, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.Mentorships.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.Status).ThenBy(m => m.Mentee!.DisplayName)
            .Select(m => new MentorMenteeListItem(m.Id, m.Mentee!.DisplayName, m.Mentee.Email!, m.Status, m.Goal != null, m.CreatedAtUtc))
            .ToListAsync(ct);
        return items;
    }

    public async Task<Result<MentorMenteeDetail>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Mentorships.AsNoTracking()
            .Where(m => m.Id == mentorshipId && m.WorkspaceId == acc.WorkspaceId)
            .Select(m => new MentorMenteeDetail(m.Id, m.Mentee!.DisplayName, m.Mentee.Email!, m.Status, m.Goal, m.CurrentLevel,
                m.Challenges, m.WeeklyHours, m.MentorPrivateNotes, m.CreatedAtUtc))
            .FirstAsync(ct);
    }

    public async Task<Result> UpdatePrivateNotesAsync(Guid actorUserId, Guid mentorshipId, string? notesMarkdown, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        var errors = new FieldErrors();
        errors.Optional("Notes", notesMarkdown, InputLimits.Markdown, "Notatki");
        if (errors.HasErrors) return errors.ToError("mentorship.invalid_notes");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var notes = Text.NullIfBlank(notesMarkdown);
        await db.Mentorships.Where(m => m.Id == mentorshipId && m.WorkspaceId == acc.WorkspaceId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.MentorPrivateNotes, notes).SetProperty(m => m.UpdatedAtUtc, now), ct);
        return Result.Success();
    }

    public async Task<Result> ChangeStatusAsync(Guid actorUserId, Guid mentorshipId, MentorshipStatus status, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        if (!Enum.IsDefined(status)) return Errors.Validation("mentorship.invalid_status", "Nieprawidłowy status.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Mentorships.Where(m => m.Id == mentorshipId && m.WorkspaceId == acc.WorkspaceId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, status).SetProperty(m => m.UpdatedAtUtc, now), ct);
        return Result.Success();
    }
}
