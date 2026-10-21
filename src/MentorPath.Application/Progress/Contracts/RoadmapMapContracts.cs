using MentorPath.Application.Common;

namespace MentorPath.Application.Progress.Contracts;

public interface IRoadmapMapService
{
    /// <summary>Published roadmap as a map for the mentee. Mentor-private data and withdrawn items are never included.</summary>
    Task<Result<RoadmapMapDto>> GetMapForMenteeAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);

    /// <summary>Roadmap (draft or published) as a map for the owning mentor, including withdrawn items.</summary>
    Task<Result<RoadmapMapDto>> GetMapForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
}

public enum MapNodeState { NotStarted, InProgress, PendingReview, NeedsRevision, Completed, Withdrawn, NotInPlan }

public sealed record RoadmapMapDto(Guid MentorshipId, bool HasRoadmap, string? RoadmapTitle, int CompletedTopics, int CountedTopics,
    int PercentComplete, IReadOnlyList<RoadmapMapNodeDto> Nodes);

/// <summary>
/// <see cref="TopicId"/> identifies the node on the map; <see cref="ParentTopicId"/> is its nearest ancestor shown on the map (null for top-level).
/// Sub-topics outside the plan have <see cref="MapNodeState.NotInPlan"/>, an empty <see cref="RoadmapItemId"/> and only a title.
/// </summary>
public sealed record RoadmapMapNodeDto(Guid RoadmapItemId, string Title, int Position, MapNodeState State, bool IsCurrent,
    int CompletedRequired, int TotalRequired, IReadOnlyList<RoadmapMapAssignmentDto> Assignments,
    Guid TopicId = default, Guid? ParentTopicId = null);

public sealed record RoadmapMapAssignmentDto(Guid AssignmentId, string ExerciseTitle, bool IsRequired, AssignmentProgressStatus Status);
