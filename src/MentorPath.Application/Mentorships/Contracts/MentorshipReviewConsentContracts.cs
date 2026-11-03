using MentorPath.Application.Common;

namespace MentorPath.Application.Mentorships.Contracts;

public interface IMentorshipReviewConsentService
{
    Task<Result<MentorshipReviewConsentDto>> GetAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result<MentorshipReviewConsentDto>> GrantAsync(Guid actorUserId, GrantMentorshipReviewConsentRequest request, CancellationToken ct);
    Task<Result<MentorshipReviewConsentDto>> RevokeAsync(Guid actorUserId, RevokeMentorshipReviewConsentRequest request, CancellationToken ct);
}

public sealed record MentorshipReviewConsentDto(Guid MentorshipId, string? DisclosureVersion,
    DateTime? GrantedAtUtc, DateTime? RevokedAtUtc, long? Version);
public sealed record GrantMentorshipReviewConsentRequest(Guid MentorshipId, string DisclosureVersion, long? ExpectedVersion);
public sealed record RevokeMentorshipReviewConsentRequest(Guid MentorshipId, long ExpectedVersion);
