namespace MentorPath.Web.BackgroundJobs;

internal interface IPostgresAdvisoryLock
{
    ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken);
}
