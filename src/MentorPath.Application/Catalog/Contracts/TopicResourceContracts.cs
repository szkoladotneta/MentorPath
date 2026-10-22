using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Catalog.Contracts;

public interface ITopicResourceService
{
    Task<Result<IReadOnlyList<TopicResourceDto>>> GetAsync(Guid actorUserId, Guid topicId, CancellationToken ct);
    Task<Result> ReplaceAsync(Guid actorUserId, Guid topicId, IReadOnlyList<TopicResourceInput> resources, CancellationToken ct);
}

// Input order defines positions; replacement is atomic and scoped to the owner's topic.
public sealed record TopicResourceInput(string Title, string Url, TopicResourceKind Kind, string? Description);

public sealed record TopicResourceDto(Guid TopicResourceId, string Title, string Url, TopicResourceKind Kind,
    string? Description, int Position);
