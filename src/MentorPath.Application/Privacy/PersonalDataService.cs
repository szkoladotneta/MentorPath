using MentorPath.Application.Common;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Privacy.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MentorPath.Application.Privacy;

/// <summary>
/// Operator CLI only (D-42). Anonymization uses the scoped <see cref="AppDbContext"/> that <see cref="UserManager{TUser}"/> also uses,
/// so content scrubbing and Identity changes commit in one transaction (same pattern as MentorProvisioningService).
/// </summary>
internal sealed class PersonalDataService(
    AppDbContext db,
    IDbContextFactory<AppDbContext> dbFactory,
    UserManager<AppUser> userManager,
    IFileStorage storage,
    TimeProvider clock,
    ILogger<PersonalDataService> logger) : IPersonalDataService
{
    private const string Module = "privacy";
    internal const int ExportSchemaVersion = 1;

    public async Task<Result<PersonalDataExport>> ExportAsync(PersonalDataSubject subject, CancellationToken ct)
    {
        var resolved = await ResolveAsync(subject, ct);
        if (resolved.Error is not null) return resolved.Error;
        var user = resolved.User!;

        await using var read = await dbFactory.CreateDbContextAsync(ct);
        var roles = (await userManager.GetRolesAsync(user)).Order(StringComparer.Ordinal).ToList();

        var mentorships = await read.Mentorships.AsNoTracking()
            .Where(m => m.MenteeUserId == user.Id)
            .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
            .Select(m => new MentorshipExport(m.Id, m.Workspace!.Name, m.Goal, m.CurrentLevel, m.WeeklyHours, m.Challenges,
                m.Status, m.CreatedAtUtc, m.UpdatedAtUtc))
            .ToListAsync(ct);

        var submissions = await read.Submissions.AsNoTracking()
            .Where(s => s.MenteeUserId == user.Id)
            .OrderBy(s => s.SubmittedAtUtc).ThenBy(s => s.AttemptNumber).ThenBy(s => s.Id)
            .Select(s => new SubmissionExport(
                s.Id,
                s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId,
                s.Assignment.RoadmapItem.Topic!.Title,
                s.Assignment.Exercise!.Title,
                s.Assignment.Exercise.Type,
                s.AttemptNumber,
                s.Status,
                s.SelectedQuizOption != null ? s.SelectedQuizOption.Text : null,
                s.DecisionAnswerJson,
                s.AnswerMarkdown,
                s.ExternalUrl,
                s.ConfidenceBefore,
                s.SubmittedAtUtc,
                s.Attachment == null ? null : new AttachmentExport(s.Attachment.ObjectKey, s.Attachment.OriginalFileName,
                    s.Attachment.ContentType, s.Attachment.SizeBytes, s.Attachment.CreatedAtUtc),
                s.Feedback == null ? null : new FeedbackExport(s.Feedback.Verdict, s.Feedback.StrengthsMarkdown,
                    s.Feedback.ImprovementsMarkdown, s.Feedback.ChallengeQuestionMarkdown, s.Feedback.CreatedAtUtc),
                s.Reflection == null ? null : new ReflectionExport(s.Reflection.ConfidenceAfter, s.Reflection.TakeawayMarkdown,
                    s.Reflection.CreatedAtUtc)))
            .ToListAsync(ct);

        var normalizedEmail = user.NormalizedEmail ?? "";
        var invitations = await read.Invitations.AsNoTracking()
            .Where(i => i.NormalizedEmail == normalizedEmail)
            .OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id)
            .Select(i => new InvitationExport(i.Id, i.Workspace!.Name, i.ExpiresAtUtc, i.AcceptedAtUtc, i.CreatedAtUtc))
            .ToListAsync(ct);

        return new PersonalDataExport(
            ExportSchemaVersion,
            clock.GetUtcNow().UtcDateTime,
            new ProfileExport(user.Id, user.Email ?? "", user.DisplayName, user.CreatedAtUtc, roles),
            mentorships, submissions, invitations);
    }

    public async Task<Result<AnonymizationReport>> AnonymizeAsync(PersonalDataSubject subject, bool apply, CancellationToken ct)
    {
        var resolved = await ResolveAsync(subject, ct);
        if (resolved.Error is not null) return resolved.Error;
        var user = resolved.User!;
        var userId = user.Id;
        var alreadyAnonymized = AnonymizedIdentity.IsAnonymized(user);
        var normalizedEmail = user.NormalizedEmail ?? "";

        var submissionIds = await db.Submissions.AsNoTracking()
            .Where(s => s.MenteeUserId == userId).Select(s => s.Id).ToListAsync(ct);
        var attachments = await db.SubmissionAttachments.AsNoTracking()
            .Where(a => submissionIds.Contains(a.SubmissionId)).Select(a => new { a.Id, a.ObjectKey }).ToListAsync(ct);

        var report = new AnonymizationReport(
            userId,
            Applied: apply,
            AlreadyAnonymized: alreadyAnonymized,
            MentorshipsClosed: await db.Mentorships.CountAsync(m => m.MenteeUserId == userId && m.Status != MentorshipStatus.Closed, ct),
            MentorshipsScrubbed: await db.Mentorships.CountAsync(m => m.MenteeUserId == userId, ct),
            SubmissionsScrubbed: submissionIds.Count,
            AttachmentsDeleted: attachments.Count,
            FeedbackScrubbed: await db.MentorFeedback.CountAsync(f => submissionIds.Contains(f.SubmissionId), ct),
            ReflectionsScrubbed: await db.SubmissionReflections.CountAsync(r => submissionIds.Contains(r.SubmissionId), ct),
            InvitationsScrubbed: await db.Invitations.CountAsync(i => i.NormalizedEmail == normalizedEmail, ct));
        if (!apply) return report;

        // Files first: a storage failure leaves the database untouched, and a re-run tolerates objects that are already gone.
        try
        {
            foreach (var attachment in attachments)
                await storage.DeleteAsync(attachment.ObjectKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Attachment deletion failed during anonymization; database unchanged. UserId={UserId}", userId);
            return new AppError(ErrorKind.Unavailable, "privacy.storage_failed",
                "Nie udało się usunąć plików. Baza danych nie została zmieniona; uruchom polecenie ponownie.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var placeholderEmail = AnonymizedIdentity.EmailFor(userId);
        var placeholderNormalized = userManager.NormalizeEmail(placeholderEmail);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.Mentorships
            .Where(m => m.MenteeUserId == userId && m.Status != MentorshipStatus.Closed)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.Status, MentorshipStatus.Closed).SetProperty(m => m.UpdatedAtUtc, now), ct);
        await db.Mentorships
            .Where(m => m.MenteeUserId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Goal, (string?)null)
                .SetProperty(m => m.Challenges, (string?)null)
                .SetProperty(m => m.MentorPrivateNotes, (string?)null), ct);

        await db.Submissions
            .Where(s => s.MenteeUserId == userId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AnswerMarkdown, (string?)null)
                .SetProperty(s => s.DecisionAnswerJson, (string?)null)
                .SetProperty(s => s.ExternalUrl, (string?)null), ct);
        await db.SubmissionAttachments
            .Where(a => submissionIds.Contains(a.SubmissionId))
            .ExecuteDeleteAsync(ct);
        await db.MentorFeedback
            .Where(f => submissionIds.Contains(f.SubmissionId))
            .ExecuteUpdateAsync(u => u
                .SetProperty(f => f.StrengthsMarkdown, "")
                .SetProperty(f => f.ImprovementsMarkdown, "")
                .SetProperty(f => f.ChallengeQuestionMarkdown, ""), ct);
        await db.SubmissionReflections
            .Where(r => submissionIds.Contains(r.SubmissionId))
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.TakeawayMarkdown, ""), ct);
        await db.Invitations
            .Where(i => i.NormalizedEmail == normalizedEmail)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.NormalizedEmail, placeholderNormalized), ct);

        if (!alreadyAnonymized)
        {
            var error = await AnonymizeAccountAsync(user, placeholderEmail);
            if (error is not null)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return error;
            }
        }

        await tx.CommitAsync(ct);
        return report;
    }

    private async Task<AppError?> AnonymizeAccountAsync(AppUser user, string placeholderEmail)
    {
        user.DisplayName = AnonymizedIdentity.DisplayName;
        user.Email = placeholderEmail;
        user.UserName = placeholderEmail;
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.TwoFactorEnabled = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.AccessFailedCount = 0;

        IdentityResult result = await userManager.UpdateAsync(user);
        if (result.Succeeded && await userManager.HasPasswordAsync(user)) result = await userManager.RemovePasswordAsync(user);
        if (result.Succeeded)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Count > 0) result = await userManager.RemoveFromRolesAsync(user, roles);
        }
        foreach (var login in result.Succeeded ? await userManager.GetLoginsAsync(user) : [])
        {
            result = await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            if (!result.Succeeded) break;
        }
        if (result.Succeeded)
        {
            var claims = await userManager.GetClaimsAsync(user);
            if (claims.Count > 0) result = await userManager.RemoveClaimsAsync(user, claims);
        }
        foreach (var tokenName in result.Succeeded ? AuthenticatorTokenNames : [])
        {
            result = await userManager.RemoveAuthenticationTokenAsync(user, InternalTokenProvider, tokenName);
            if (!result.Succeeded) break;
        }
        if (result.Succeeded) result = await userManager.UpdateSecurityStampAsync(user);

        if (result.Succeeded) return null;
        logger.LogError("Account anonymization failed. UserId={UserId} Codes={Codes}", user.Id, string.Join(",", result.Errors.Select(e => e.Code)));
        return Errors.Conflict("privacy.account_update_failed", "Nie udało się zanonimizować konta. Baza danych nie została zmieniona.");
    }

    // Tokens Identity stores for the user itself (authenticator key, recovery codes); external-login tokens go with RemoveLoginAsync.
    private const string InternalTokenProvider = "[AspNetUserStore]";
    private static readonly string[] AuthenticatorTokenNames = ["AuthenticatorKey", "RecoveryCodes"];

    private sealed record Resolved(AppUser? User, AppError? Error);

    private async Task<Resolved> ResolveAsync(PersonalDataSubject subject, CancellationToken ct)
    {
        var hasId = subject.UserId is { } id && id != Guid.Empty;
        var hasEmail = !string.IsNullOrWhiteSpace(subject.Email);
        if (hasId == hasEmail)
            return new(null, Errors.Validation("privacy.invalid_request", "Podaj dokładnie jedno z: identyfikator użytkownika albo e-mail."));

        var user = hasId
            ? await userManager.FindByIdAsync(subject.UserId!.Value.ToString())
            : await userManager.FindByEmailAsync(subject.Email!.Trim());
        if (user is null) return new(null, Errors.NotFound(Module));

        if (await db.Workspaces.AnyAsync(w => w.OwnerUserId == user.Id, ct)
            || !await db.Mentorships.AnyAsync(m => m.MenteeUserId == user.Id, ct))
        {
            return new(null, Errors.Validation("privacy.unsupported_subject",
                "Procedura obejmuje wyłącznie mentees; właściciele workspace'ów i konta bez relacji mentoringowej nie są obsługiwane."));
        }
        return new(user, null);
    }
}
