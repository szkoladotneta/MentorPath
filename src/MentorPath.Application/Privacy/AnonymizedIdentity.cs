using MentorPath.Data;

namespace MentorPath.Application.Privacy;

/// <summary>Placeholder identity of an anonymized account (D-42). Detection is by exact value only, never by domain.</summary>
internal static class AnonymizedIdentity
{
    public const string DisplayName = "Usunięty użytkownik";
    private const string Domain = "anonymized.invalid";

    public static string EmailFor(Guid userId) => $"deleted-{userId:N}@{Domain}";

    public static bool IsAnonymized(AppUser user) =>
        string.Equals(user.Email, EmailFor(user.Id), StringComparison.Ordinal);
}
