using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Mentorships;

internal sealed class MentorshipReviewConsentService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    IOptionsMonitor<McpReviewOptions> options,
    TimeProvider clock) : IMentorshipReviewConsentService
{
    private const string Module = "mentorship";

    public async Task<Result<MentorshipReviewConsentDto>> GetAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null && !await access.HasWorkspaceRelationshipAsync(actorUserId, ct)) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Closed mentees may still inspect their consent evidence, unlike ordinary mentorship projections.
        var workspaceId = acc?.WorkspaceId ?? await db.Mentorships.AsNoTracking()
            .Where(m => m.Id == mentorshipId && m.MenteeUserId == actorUserId && m.Status == MentorshipStatus.Closed)
            .Select(m => (Guid?)m.WorkspaceId).SingleOrDefaultAsync(ct);
        if (workspaceId is null) return Errors.NotFound(Module);
        var consent = await db.MentorshipReviewConsents.AsNoTracking()
            .SingleOrDefaultAsync(c => c.MentorshipId == mentorshipId && c.WorkspaceId == workspaceId, ct);
        return ToDto(mentorshipId, consent);
    }

    public async Task<Result<MentorshipReviewConsentDto>> GrantAsync(
        Guid actorUserId, GrantMentorshipReviewConsentRequest request, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, request.MentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await db.Mentorships.FromSqlInterpolated(
                $"""SELECT * FROM "Mentorships" WHERE "Id" = {request.MentorshipId} AND "WorkspaceId" = {acc.WorkspaceId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
        if (mentorship is null || mentorship.MenteeUserId != actorUserId
            || mentorship.Status is not (MentorshipStatus.Active or MentorshipStatus.Paused))
            return Errors.NotFound(Module);

        var current = options.CurrentValue;
        if (!current.Enabled)
            return Errors.Conflict("mentorship.review_consent_disabled", "Zgoda na recenzje MCP jest obecnie niedostępna.");
        if (!string.Equals(request.DisclosureVersion, current.DisclosureVersion, StringComparison.Ordinal))
            return Errors.Conflict("mentorship.review_consent_disclosure_changed", "Treść zgody uległa zmianie. Zapoznaj się z aktualną informacją.");

        var consent = await db.MentorshipReviewConsents
            .SingleOrDefaultAsync(c => c.MentorshipId == request.MentorshipId && c.WorkspaceId == acc.WorkspaceId, ct);
        if (consent is null ? request.ExpectedVersion is not null : request.ExpectedVersion != consent.Version)
            return VersionConflict();

        var now = clock.GetUtcNow().UtcDateTime;
        if (consent is null)
        {
            consent = new MentorshipReviewConsent
            {
                Id = Guid.NewGuid(), WorkspaceId = acc.WorkspaceId, MentorshipId = request.MentorshipId,
                DisclosureVersion = current.DisclosureVersion, GrantedAtUtc = now, Version = 1
            };
            db.MentorshipReviewConsents.Add(consent);
        }
        else
        {
            consent.DisclosureVersion = current.DisclosureVersion;
            consent.GrantedAtUtc = now;
            consent.RevokedAtUtc = null;
            consent.Version++;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(request.MentorshipId, consent);
    }

    public async Task<Result<MentorshipReviewConsentDto>> RevokeAsync(
        Guid actorUserId, RevokeMentorshipReviewConsentRequest request, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, request.MentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var mentorship = await db.Mentorships.FromSqlInterpolated(
                $"""SELECT * FROM "Mentorships" WHERE "Id" = {request.MentorshipId} AND "WorkspaceId" = {acc.WorkspaceId} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
        if (mentorship is null || mentorship.MenteeUserId != actorUserId
            || mentorship.Status is not (MentorshipStatus.Active or MentorshipStatus.Paused))
            return Errors.NotFound(Module);
        var consent = await db.MentorshipReviewConsents
            .SingleOrDefaultAsync(c => c.MentorshipId == request.MentorshipId && c.WorkspaceId == acc.WorkspaceId, ct);
        if (consent is null || request.ExpectedVersion != consent.Version) return VersionConflict();
        if (consent.RevokedAtUtc is not null) return ToDto(request.MentorshipId, consent);
        consent.RevokedAtUtc = clock.GetUtcNow().UtcDateTime;
        consent.Version++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(request.MentorshipId, consent);
    }

    private static AppError VersionConflict() =>
        Errors.Conflict("mentorship.review_consent_version_conflict", "Zgoda została zmieniona. Odśwież stronę i spróbuj ponownie.");

    private static MentorshipReviewConsentDto ToDto(Guid mentorshipId, MentorshipReviewConsent? consent) =>
        new(mentorshipId, consent?.DisclosureVersion, consent?.GrantedAtUtc, consent?.RevokedAtUtc, consent?.Version);
}
