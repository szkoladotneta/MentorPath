using System.Security.Cryptography;
using System.Text;
using MentorPath.Application.Common.Access;

namespace MentorPath.Application.Common;

internal static class MentorScope
{
    /// <summary>Resolves the workspace owned by the actor (D-08). Null means the actor is not a mentor.</summary>
    public static Task<Guid?> ResolveAsync(IWorkspaceAccessService access, Guid actorUserId, CancellationToken ct) =>
        access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
}

internal static class Tokens
{
    public static string NewToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal static class Text
{
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return $"{email[0]}***{email[at..]}";
    }
}
