using MentorPath.Application.Common;

namespace MentorPath.Application.Invitations.Contracts;

public interface IInvitationService
{
    Task<Result<CreateInvitationResult>> CreateAsync(Guid actorUserId, CreateInvitationRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<InvitationListItem>>> ListAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<InvitationPreview>> PreviewAsync(string token, CancellationToken ct);
    Task<Result<AcceptInvitationResult>> AcceptWithNewAccountAsync(AcceptWithNewAccountRequest request, CancellationToken ct);
    Task<Result<AcceptInvitationResult>> AcceptAsExistingUserAsync(Guid actorUserId, string token, CancellationToken ct);
}

public sealed record CreateInvitationRequest(string Email);
public sealed record CreateInvitationResult(Guid InvitationId, string AcceptUrl, DateTime ExpiresAtUtc);
public enum InvitationState { Pending, Accepted, Expired }
public sealed record InvitationListItem(Guid InvitationId, string Email, InvitationState State, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
public sealed record InvitationPreview(string WorkspaceName, string MentorDisplayName, string MaskedEmail, bool AccountExists);
public sealed record AcceptWithNewAccountRequest(string Token, string DisplayName, string Password);
public sealed record AcceptInvitationResult(Guid MentorshipId, Guid UserId);
