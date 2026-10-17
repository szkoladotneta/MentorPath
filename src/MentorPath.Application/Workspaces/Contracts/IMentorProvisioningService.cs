using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Workspaces.Contracts;

public interface IMentorProvisioningService
{
    Task<Result<ProvisionMentorResult>> ProvisionMentorAsync(ProvisionMentorRequest request, CancellationToken ct);
}

public sealed record ProvisionMentorRequest(string Email, string DisplayName, string WorkspaceName);
public sealed record ProvisionMentorResult(Guid UserId, Guid WorkspaceId, string PasswordSetupUrl);
