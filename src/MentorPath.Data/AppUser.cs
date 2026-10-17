using Microsoft.AspNetCore.Identity;

namespace MentorPath.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
