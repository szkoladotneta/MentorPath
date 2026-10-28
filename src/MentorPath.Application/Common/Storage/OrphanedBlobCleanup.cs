using MentorPath.Data;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Common.Storage;

public interface IOrphanedBlobCleanup
{
    Task<int> RunOnceAsync(CancellationToken ct);
}

internal sealed class OrphanedBlobCleanup(
    IDbContextFactory<AppDbContext> dbFactory,
    IFileStorage storage,
    TimeProvider timeProvider) : IOrphanedBlobCleanup
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var cutoffUtc = timeProvider.GetUtcNow() - GracePeriod;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var referencedKeys = await db.SubmissionAttachments.AsNoTracking()
            .Select(attachment => attachment.ObjectKey)
            .ToHashSetAsync(ct);
        var candidates = new List<string>();
        await foreach (var blob in storage.ListAsync(ct))
        {
            if (blob.LastModifiedUtc is { } lastModifiedUtc
                && lastModifiedUtc <= cutoffUtc
                && !referencedKeys.Contains(blob.ObjectKey))
            {
                candidates.Add(blob.ObjectKey);
            }
        }

        if (candidates.Count == 0) return 0;

        var stillReferencedKeys = await db.SubmissionAttachments.AsNoTracking()
            .Where(attachment => candidates.Contains(attachment.ObjectKey))
            .Select(attachment => attachment.ObjectKey)
            .ToHashSetAsync(ct);

        var deletedCount = 0;
        foreach (var objectKey in candidates)
        {
            if (stillReferencedKeys.Contains(objectKey)) continue;
            await storage.DeleteAsync(objectKey, ct);
            deletedCount++;
        }

        return deletedCount;
    }
}
