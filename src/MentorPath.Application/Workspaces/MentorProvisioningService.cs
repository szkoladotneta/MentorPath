using System.Text;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Email;
using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Workspaces;

internal sealed class MentorProvisioningService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    TimeProvider clock,
    IOptions<AppOptions> appOptions) : IMentorProvisioningService
{
    public async Task<Result<ProvisionMentorResult>> ProvisionMentorAsync(ProvisionMentorRequest request, CancellationToken ct)
    {
        var errors = new FieldErrors();
        errors.Required(nameof(request.Email), request.Email, InputLimits.Email, "E-mail");
        errors.Required(nameof(request.DisplayName), request.DisplayName, InputLimits.DisplayName, "Nazwa");
        errors.Required(nameof(request.WorkspaceName), request.WorkspaceName, InputLimits.WorkspaceName, "Workspace");
        if (errors.HasErrors) return errors.ToError("workspace.invalid_request");

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Errors.Conflict("workspace.email_taken", "Użytkownik o tym adresie już istnieje.");

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            CreatedAtUtc = now,
        };
        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
            return Errors.Validation("workspace.user_invalid", string.Join(" ", created.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, AppRoles.Mentor);

        var workspace = new Workspace
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = user.Id,
            Name = request.WorkspaceName.Trim(),
            CreatedAtUtc = now,
        };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var url = appOptions.Value.BuildUrl($"Account/ResetPassword?code={code}");
        return new ProvisionMentorResult(user.Id, workspace.Id, url);
    }
}
