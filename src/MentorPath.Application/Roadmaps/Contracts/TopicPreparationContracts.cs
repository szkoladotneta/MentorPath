using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;

namespace MentorPath.Application.Roadmaps.Contracts;

public interface ITopicPreparationService
{
    Task<Result<MenteeTopicPreparationDto>> GetTopicForMenteeAsync(Guid actorUserId, Guid mentorshipId, Guid roadmapItemId, CancellationToken ct);
}

public sealed record MenteeTopicPreparationDto(Guid MentorshipId, Guid RoadmapItemId, string TopicTitle,
    string TopicDescriptionMarkdown, string? MentorNoteMarkdown, IReadOnlyList<TopicResourceDto> Resources,
    IReadOnlyList<MenteeAssignmentListItem> Assignments);
