using MentorPath.Application.Common.Storage;

namespace MentorPath.Web.BackgroundJobs;

internal sealed class OrphanedBlobCleanupJob(
    IOrphanedBlobCleanup cleanup,
    ILogger<OrphanedBlobCleanupJob> logger) : IBackgroundJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var deletedCount = await cleanup.RunOnceAsync(cancellationToken);
        if (deletedCount > 0)
            logger.LogInformation("Removed {DeletedCount} orphaned attachment blobs.", deletedCount);
    }
}
