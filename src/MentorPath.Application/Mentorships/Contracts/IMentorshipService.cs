using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Mentorships.Contracts;

public interface IMentorshipService
{
    Task<Result<IReadOnlyList<MenteeMentorshipListItem>>> ListForMenteeAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<OnboardingDto>> GetOnboardingAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result> SaveOnboardingAsync(Guid actorUserId, Guid mentorshipId, SaveOnboardingRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<MentorMenteeListItem>>> ListForMentorAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<MentorMenteeDetail>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result> UpdatePrivateNotesAsync(Guid actorUserId, Guid mentorshipId, string? notesMarkdown, CancellationToken ct);
    Task<Result> ChangeStatusAsync(Guid actorUserId, Guid mentorshipId, MentorshipStatus status, CancellationToken ct);
}

public sealed record MenteeMentorshipListItem(Guid MentorshipId, string WorkspaceName, string MentorDisplayName, MentorshipStatus Status, bool OnboardingCompleted);
public sealed record OnboardingDto(Guid MentorshipId, string? Goal, ExperienceLevel? CurrentLevel, string? Challenges, int? WeeklyHours);
public sealed record SaveOnboardingRequest(string Goal, ExperienceLevel CurrentLevel, string Challenges, int WeeklyHours);
public sealed record MentorMenteeListItem(Guid MentorshipId, string MenteeDisplayName, string MenteeEmail, MentorshipStatus Status, bool OnboardingCompleted, DateTime CreatedAtUtc);
public sealed record MentorMenteeDetail(Guid MentorshipId, string MenteeDisplayName, string MenteeEmail, MentorshipStatus Status,
    string? Goal, ExperienceLevel? CurrentLevel, string? Challenges, int? WeeklyHours, string? MentorPrivateNotesMarkdown, DateTime CreatedAtUtc);
