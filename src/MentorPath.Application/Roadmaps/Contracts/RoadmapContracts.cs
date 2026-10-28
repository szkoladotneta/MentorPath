using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Roadmaps.Contracts;

public interface IRoadmapService
{
    Task<Result<Guid>> CreateAsync(Guid actorUserId, CreateRoadmapRequest request, CancellationToken ct);
    Task<Result<MentorRoadmapDto?>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result> UpdateDetailsAsync(Guid actorUserId, Guid roadmapId, UpdateRoadmapRequest request, CancellationToken ct);
    Task<Result<Guid>> AddTopicAsync(Guid actorUserId, AddRoadmapTopicRequest request, CancellationToken ct);
    Task<Result> UpdateTopicNoteAsync(Guid actorUserId, Guid roadmapItemId, string? mentorNoteMarkdown, CancellationToken ct);
    Task<Result> ReorderTopicsAsync(Guid actorUserId, Guid roadmapId, IReadOnlyList<Guid> orderedRoadmapItemIds, CancellationToken ct);
    Task<Result> SetTopicActiveAsync(Guid actorUserId, Guid roadmapItemId, bool isActive, CancellationToken ct);
    Task<Result> RemoveTopicAsync(Guid actorUserId, Guid roadmapItemId, CancellationToken ct);
    Task<Result<Guid>> AssignExerciseAsync(Guid actorUserId, AssignExerciseRequest request, CancellationToken ct);
    Task<Result> UpdateAssignmentAsync(Guid actorUserId, Guid assignmentId, UpdateAssignmentRequest request, CancellationToken ct);
    Task<Result> ReorderAssignmentsAsync(Guid actorUserId, Guid roadmapItemId, IReadOnlyList<Guid> orderedAssignmentIds, CancellationToken ct);
    Task<Result> SetAssignmentActiveAsync(Guid actorUserId, Guid assignmentId, bool isActive, CancellationToken ct);
    Task<Result> RemoveAssignmentAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
    Task<Result> PublishAsync(Guid actorUserId, Guid roadmapId, CancellationToken ct);
    Task<Result<MenteeRoadmapDto>> GetForMenteeAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
}

public sealed record CreateRoadmapRequest(Guid MentorshipId, string Title, string ObjectiveMarkdown);
public sealed record UpdateRoadmapRequest(string Title, string ObjectiveMarkdown);
public sealed record AddRoadmapTopicRequest(Guid RoadmapId, Guid TopicId, string? MentorNoteMarkdown);
public sealed record AssignExerciseRequest(Guid RoadmapItemId, Guid ExerciseId, bool IsRequired, DateTime? DueAtUtc);
public sealed record UpdateAssignmentRequest(bool IsRequired, DateTime? DueAtUtc);
public sealed record SetCompetencyStatusRequest(CompetencyStatus Status, string? ManualJustification);

public sealed record MentorRoadmapDto(Guid RoadmapId, Guid MentorshipId, string Title, string ObjectiveMarkdown, RoadmapStatus Status,
    DateTime? PublishedAtUtc, IReadOnlyList<MentorRoadmapItemDto> Items);
public sealed record MentorRoadmapItemDto(Guid RoadmapItemId, Guid TopicId, string TopicTitle, int Position, bool IsActive,
    string? MentorNoteMarkdown, bool HasSubmissions, IReadOnlyList<MentorAssignmentDto> Assignments,
    CompetencyStatus Status = CompetencyStatus.NotAssessed,
    IReadOnlyList<CompetencyStatusHistoryDto>? StatusHistory = null,
    IReadOnlyList<CompetencyEvidenceDto>? Evidence = null);
public sealed record MentorAssignmentDto(Guid AssignmentId, Guid ExerciseId, string ExerciseTitle, ExerciseType Type,
    int Position, bool IsRequired, bool IsActive, DateTime? DueAtUtc, int SubmissionCount);
public sealed record CompetencyStatusHistoryDto(Guid HistoryId, CompetencyStatus PreviousStatus, CompetencyStatus NewStatus,
    Guid ChangedByUserId, DateTime ChangedAtUtc, string? ManualJustification);
public sealed record CompetencyEvidenceDto(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status, DateTime SubmittedAtUtc);

public sealed record MenteeRoadmapDto(Guid RoadmapId, Guid MentorshipId, string Title, string ObjectiveMarkdown, IReadOnlyList<MenteeRoadmapItemDto> Items);
public sealed record MenteeRoadmapItemDto(Guid RoadmapItemId, string TopicTitle, string TopicDescriptionMarkdown,
    string? MentorNoteMarkdown, int Position, IReadOnlyList<MenteeAssignmentListItem> Assignments,
    CompetencyStatus Status = CompetencyStatus.NotAssessed);
public sealed record MenteeAssignmentListItem(Guid AssignmentId, string ExerciseTitle, ExerciseType Type, bool IsRequired,
    DateTime? DueAtUtc, SubmissionStatus? LatestStatus, int AttemptCount);
