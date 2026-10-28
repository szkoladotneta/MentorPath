using Microsoft.Extensions.Options;

namespace MentorPath.Web.BackgroundJobs;

internal sealed class BackgroundJobWorker(
    IServiceScopeFactory scopeFactory,
    IPostgresAdvisoryLock advisoryLock,
    IOptions<BackgroundJobOptions> options,
    TimeProvider timeProvider,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunTickAsync(stoppingToken);
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetServices<IBackgroundJob>().ToArray();
        if (jobs.Length == 0)
            return;

        await using var lease = await advisoryLock.TryAcquireAsync(cancellationToken);
        if (lease is null)
        {
            logger.LogDebug("Skipping background job tick because another process holds the scheduler lock.");
            return;
        }

        foreach (var job in jobs)
        {
            try
            {
                await job.ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job {JobType} failed.", job.GetType().FullName);
            }
        }
    }
}
