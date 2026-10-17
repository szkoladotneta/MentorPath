using MentorPath.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/Logout", async (
            [FromServices] SignInManager<AppUser> signInManager,
            [FromForm] string? returnUrl) =>
        {
            await signInManager.SignOutAsync();
            var target = string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) || returnUrl.StartsWith("//", StringComparison.Ordinal)
                ? "~/"
                : $"~/{returnUrl.TrimStart('/')}";
            return TypedResults.LocalRedirect(target);
        });

        return accountGroup;
    }
}
