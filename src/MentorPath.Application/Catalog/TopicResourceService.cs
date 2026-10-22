using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Data;
using MentorPath.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Catalog;

internal sealed class TopicResourceService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access) : ITopicResourceService
{
    private const string Module = "catalog";

    public async Task<Result<IReadOnlyList<TopicResourceDto>>> GetAsync(Guid actorUserId, Guid topicId, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exists = await db.Topics.AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.WorkspaceId == workspaceId, ct);
        if (!exists) return Errors.NotFound(Module);

        var resources = await db.TopicResources.AsNoTracking()
            .Where(r => r.TopicId == topicId && r.WorkspaceId == workspaceId)
            .OrderBy(r => r.Position)
            .Select(r => new TopicResourceDto(r.Id, r.Title, r.Url, r.Kind, r.Description, r.Position))
            .ToListAsync(ct);
        return resources;
    }

    public async Task<Result> ReplaceAsync(Guid actorUserId, Guid topicId, IReadOnlyList<TopicResourceInput> resources, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var topic = await db.Topics
            .FromSqlInterpolated($"""SELECT * FROM "Topics" WHERE "Id" = {topicId} AND "WorkspaceId" = {workspaceId.Value} FOR UPDATE""")
            .SingleOrDefaultAsync(ct);
        if (topic is null) return Errors.NotFound(Module);

        var errors = TopicResourceValidator.Validate(resources);
        if (errors.HasErrors) return errors.ToError("catalog.invalid_resources");

        await db.TopicResources
            .Where(r => r.TopicId == topicId && r.WorkspaceId == workspaceId.Value)
            .ExecuteDeleteAsync(ct);
        for (var position = 0; position < resources.Count; position++)
        {
            var resource = resources[position];
            db.TopicResources.Add(new TopicResource
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId.Value,
                TopicId = topicId,
                Title = resource.Title.Trim(),
                Url = resource.Url.Trim(),
                Kind = resource.Kind,
                Description = string.IsNullOrWhiteSpace(resource.Description) ? null : resource.Description.Trim(),
                Position = position,
            });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}
