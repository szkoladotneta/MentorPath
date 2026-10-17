using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Common.Email;
using MentorPath.Application.Invitations.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Invitations;

/// <summary>
/// Uses the request-scoped AppDbContext (shared with UserManager) so account creation,
/// invitation consumption and mentorship creation commit in one transaction.
/// Called only from static SSR pages (HTTP request scope), never from a long-lived circuit.
/// </summary>
internal sealed class InvitationService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IWorkspaceAccessService access,
    TimeProvider clock,
    IOptions<AppOptions> appOptions) : IInvitationService
{
    internal static readonly TimeSpan Validity = TimeSpan.FromDays(7);
    private const string Module = "invitation";

    public async Task<Result<CreateInvitationResult>> CreateAsync(Guid actorUserId, CreateInvitationRequest request, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);

        var errors = new FieldErrors();
        errors.Required(nameof(request.Email), request.Email, InputLimits.Email, "E-mail");
        if (!errors.HasErrors && !System.Net.Mail.MailAddress.TryCreate(request.Email.Trim(), out _))
            errors.Add(nameof(request.Email), "Niepoprawny adres e-mail.");
        if (errors.HasErrors) return errors.ToError("invitation.invalid_request");

        var normalized = userManager.NormalizeEmail(request.Email.Trim());
        var now = clock.GetUtcNow().UtcDateTime;

        var mentor = await db.Users.AsNoTracking().FirstAsync(u => u.Id == actorUserId, ct);
        if (mentor.NormalizedEmail == normalized)
            return Errors.Validation("invitation.self_invite", "Nie możesz zaprosić samego siebie.");

        var alreadyActive = await db.Mentorships.AsNoTracking().AnyAsync(m =>
            m.WorkspaceId == workspaceId && m.Mentee!.NormalizedEmail == normalized && m.Status != MentorshipStatus.Closed, ct);
        if (alreadyActive)
            return Errors.Conflict("invitation.already_member", "Ta osoba jest już mentee w Twoim workspace.");

        // D-27: a new invitation expires previous pending ones for the same e-mail.
        await db.Invitations
            .Where(i => i.WorkspaceId == workspaceId && i.NormalizedEmail == normalized && i.AcceptedAtUtc == null && i.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAtUtc, now), ct);

        var token = Tokens.NewToken();
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId.Value,
            InvitedByUserId = actorUserId,
            NormalizedEmail = normalized,
            TokenHash = Tokens.Hash(token),
            ExpiresAtUtc = now + Validity,
            CreatedAtUtc = now,
        };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);

        return new CreateInvitationResult(invitation.Id, appOptions.Value.BuildUrl($"invite/{token}"), invitation.ExpiresAtUtc);
    }

    public async Task<Result<IReadOnlyList<InvitationListItem>>> ListAsync(Guid actorUserId, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Invitations.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return rows.Select(i => new InvitationListItem(i.Id, i.NormalizedEmail.ToLowerInvariant(),
                i.AcceptedAtUtc is not null ? InvitationState.Accepted : i.ExpiresAtUtc <= now ? InvitationState.Expired : InvitationState.Pending,
                i.CreatedAtUtc, i.ExpiresAtUtc))
            .ToList();
    }

    public async Task<Result<InvitationPreview>> PreviewAsync(string token, CancellationToken ct)
    {
        var invitation = await FindValidAsync(token, ct);
        if (invitation is null) return InvalidToken();
        var ws = await db.Workspaces.AsNoTracking().Where(w => w.Id == invitation.WorkspaceId)
            .Select(w => new { w.Name, Mentor = w.Owner!.DisplayName }).FirstAsync(ct);
        var exists = await db.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == invitation.NormalizedEmail, ct);
        return new InvitationPreview(ws.Name, ws.Mentor, Text.MaskEmail(invitation.NormalizedEmail.ToLowerInvariant()), exists);
    }

    public async Task<Result<AcceptInvitationResult>> AcceptWithNewAccountAsync(AcceptWithNewAccountRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        errors.Required(nameof(request.DisplayName), request.DisplayName, InputLimits.DisplayName, "Imię i nazwisko");
        errors.Required(nameof(request.Password), request.Password, 200, "Hasło");
        if (errors.HasErrors) return errors.ToError("invitation.invalid_request");

        var invitation = await FindValidAsync(request.Token, ct);
        if (invitation is null) return InvalidToken();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == invitation.NormalizedEmail, ct))
            return Errors.Conflict("invitation.account_exists", "Konto z tym adresem już istnieje — zaloguj się, aby przyjąć zaproszenie.");

        await EnsureRolesAsync();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await ConsumeAsync(invitation.Id, ct)) return InvalidToken();

        var email = invitation.NormalizedEmail.ToLowerInvariant();
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true, // D-11: possession of the invitation token confirms the e-mail.
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            await tx.RollbackAsync(ct);
            var fields = new Dictionary<string, string[]> { [nameof(request.Password)] = created.Errors.Select(e => e.Description).ToArray() };
            return Errors.Validation("invitation.password_invalid", "Hasło nie spełnia wymagań.", fields);
        }
        await userManager.AddToRoleAsync(user, AppRoles.Mentee);
        var mentorshipId = await UpsertMentorshipAsync(invitation.WorkspaceId, user.Id, ct);
        await tx.CommitAsync(ct);
        return new AcceptInvitationResult(mentorshipId, user.Id);
    }

    public async Task<Result<AcceptInvitationResult>> AcceptAsExistingUserAsync(Guid actorUserId, string token, CancellationToken ct)
    {
        var invitation = await FindValidAsync(token, ct);
        if (invitation is null) return InvalidToken();

        var user = await userManager.FindByIdAsync(actorUserId.ToString());
        if (user is null) return Errors.NotFound(Module);
        if (!string.Equals(user.NormalizedEmail, invitation.NormalizedEmail, StringComparison.Ordinal))
            return Errors.Validation("invitation.email_mismatch", "To zaproszenie zostało wysłane na inny adres e-mail. Zaloguj się właściwym kontem lub poproś mentora o nowe zaproszenie.");

        var isOwner = await db.Workspaces.AnyAsync(w => w.Id == invitation.WorkspaceId && w.OwnerUserId == actorUserId, ct);
        if (isOwner) return Errors.Validation("invitation.self_invite", "Nie możesz dołączyć do własnego workspace jako mentee.");

        var existing = await db.Mentorships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == invitation.WorkspaceId && m.MenteeUserId == actorUserId, ct);
        if (existing is not null && existing.Status != MentorshipStatus.Closed)
            return Errors.Conflict("invitation.already_member", "Jesteś już mentee w tym workspace.");

        await EnsureRolesAsync();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await ConsumeAsync(invitation.Id, ct)) return InvalidToken();
        if (!await userManager.IsInRoleAsync(user, AppRoles.Mentee))
            await userManager.AddToRoleAsync(user, AppRoles.Mentee);
        var mentorshipId = await UpsertMentorshipAsync(invitation.WorkspaceId, user.Id, ct);
        await tx.CommitAsync(ct);
        return new AcceptInvitationResult(mentorshipId, user.Id);
    }

    private async Task<Invitation?> FindValidAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = Tokens.Hash(token);
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Invitations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAtUtc == null && i.ExpiresAtUtc > now, ct);
    }

    private async Task<bool> ConsumeAsync(Guid invitationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var rows = await db.Invitations
            .Where(i => i.Id == invitationId && i.AcceptedAtUtc == null && i.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAtUtc, now), ct);
        return rows == 1;
    }

    private async Task<Guid> UpsertMentorshipAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.Mentorships.FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.MenteeUserId == userId, ct);
        if (existing is not null)
        {
            existing.Status = MentorshipStatus.Active; // D-12: re-invite reactivates a closed relation.
            existing.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            return existing.Id;
        }

        var mentorship = new Mentorship
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            MenteeUserId = userId,
            Status = MentorshipStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.Mentorships.Add(mentorship);
        await db.SaveChangesAsync(ct);
        return mentorship.Id;
    }

    private async Task EnsureRolesAsync()
    {
        if (!await roleManager.RoleExistsAsync(AppRoles.Mentee))
            await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Mentee));
    }

    private static AppError InvalidToken() =>
        Errors.Validation("invitation.invalid_or_used", "Zaproszenie jest nieprawidłowe, wygasło lub zostało już wykorzystane.");
}
