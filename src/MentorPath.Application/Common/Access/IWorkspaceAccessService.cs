using MentorPath.Data.Enums;

namespace MentorPath.Application.Common.Access;

public enum AccessRole { WorkspaceOwner, Mentee }

public sealed record MentorshipAccess(
    Guid MentorshipId, Guid WorkspaceId, Guid MenteeUserId,
    MentorshipStatus Status, AccessRole Role)
{
    public bool MenteeCanWrite => Role == AccessRole.Mentee && Status == MentorshipStatus.Active;
}

public sealed record AssignmentAccess(
    Guid AssignmentId, Guid WorkspaceId, Guid MentorshipId, Guid MenteeUserId,
    Guid ExerciseId, AccessRole Role,
    MentorshipStatus MentorshipStatus, RoadmapStatus RoadmapStatus,
    bool AssignmentIsActive, bool RoadmapItemIsActive);

public interface IWorkspaceAccessService
{
    Task<Guid?> GetOwnedWorkspaceIdAsync(Guid userId, CancellationToken ct);
    /// <summary>True when the user owns a workspace or is the mentee of any mentorship, whatever its status (D-56).</summary>
    Task<bool> HasWorkspaceRelationshipAsync(Guid userId, CancellationToken ct);
    Task<bool> CanManageWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct);
    Task<MentorshipAccess?> GetMentorshipAccessAsync(Guid userId, Guid mentorshipId, CancellationToken ct);
    Task<bool> CanAccessMentorshipAsync(Guid userId, Guid mentorshipId, CancellationToken ct);
    Task<AssignmentAccess?> GetAssignmentAccessAsync(Guid userId, Guid assignmentId, CancellationToken ct);
    Task<bool> CanAccessSubmissionAsync(Guid userId, Guid submissionId, CancellationToken ct);
}
