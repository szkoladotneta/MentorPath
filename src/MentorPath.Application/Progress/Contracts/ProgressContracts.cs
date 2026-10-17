using MentorPath.Application.Common;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Progress.Contracts;

public interface IProgressService
{
    Task<Result<RoadmapProgressDto>> GetRoadmapProgressAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result<MentorDashboardDto>> GetMentorDashboardAsync(Guid actorUserId, CancellationToken ct);
}

public enum AssignmentProgressStatus { NotStarted, InProgress, PendingReview, NeedsRevision, Completed }
public enum TopicProgressStatus { NotStarted, InProgress, Completed }

public sealed record RoadmapProgressDto(Guid MentorshipId, bool HasPublishedRoadmap, int CompletedTopics, int CountedTopics,
    int PercentComplete, IReadOnlyList<TopicProgressDto> Topics);
public sealed record TopicProgressDto(Guid RoadmapItemId, string TopicTitle, TopicProgressStatus Status, bool CountsTowardsProgress,
    int CompletedRequired, int TotalRequired, IReadOnlyList<AssignmentProgressDto> Assignments);
public sealed record AssignmentProgressDto(Guid AssignmentId, string ExerciseTitle, bool IsRequired, AssignmentProgressStatus Status,
    int? LatestConfidenceBefore, int? LatestConfidenceAfter);

public sealed record MentorDashboardDto(IReadOnlyList<MenteeProgressRow> Mentees, IReadOnlyList<PendingReviewItem> PendingReviews,
    IReadOnlyList<ActivityItem> RecentActivity);
public sealed record MenteeProgressRow(Guid MentorshipId, string MenteeDisplayName, MentorshipStatus Status, bool HasPublishedRoadmap,
    int CompletedTopics, int CountedTopics, int PendingReviewCount, DateTime? LastSubmissionAtUtc);
public enum ActivityKind { Submitted, FeedbackGiven, ReflectionAdded }
public sealed record ActivityItem(DateTime OccurredAtUtc, ActivityKind Kind, Guid MentorshipId, string MenteeDisplayName, string ExerciseTitle, Guid SubmissionId);
