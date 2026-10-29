using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Web.Mcp;

// Named result types for get_roadmap and the write tools (D-64): the SDK publishes an output schema for them, and the architecture
// tests can walk every result type for fields an agent must never see. Anonymous objects would be invisible to both.

public sealed record McpTopicCreated(Guid TopicId, string Title, string Slug);

public sealed record McpExerciseDraftCreated(Guid ExerciseId, ExerciseStatus Status, ExerciseSource Source, string NextStep);

/// <summary>The roadmap when the mentorship has one; otherwise <see cref="Roadmap"/> is null and <see cref="Hint"/> says what to do.</summary>
public sealed record McpRoadmapView(Guid MentorshipId, MentorRoadmapDto? Roadmap, string? Hint);

public sealed record McpRoadmapCreated(Guid RoadmapId, RoadmapStatus Status, string NextStep);

public sealed record McpRoadmapTopicAdded(Guid RoadmapItemId, Guid RoadmapId, Guid TopicId);

public sealed record McpExerciseAssigned(Guid AssignmentId, Guid RoadmapItemId, Guid ExerciseId, bool IsRequired, DateTime? DueAtUtc);

public sealed record McpRoadmapReordered(Guid RoadmapId, IReadOnlyList<Guid> OrderedRoadmapItemIds);
