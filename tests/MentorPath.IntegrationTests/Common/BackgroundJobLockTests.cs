using MentorPath.Web.BackgroundJobs;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace MentorPath.IntegrationTests.Common;

public sealed class BackgroundJobLockTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Advisory_lock_skips_a_competing_worker_for_the_same_identity()
    {
        var firstWorker = new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey);
        var competingWorker = new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey);

        var holder = await firstWorker.TryAcquireAsync(CancellationToken.None);
        holder.ShouldNotBeNull();
        await using (holder!)
        {
            var competingLease = await competingWorker.TryAcquireAsync(CancellationToken.None);
            competingLease.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Independent_workers_never_run_protected_work_concurrently()
    {
        var protectedWork = new BlockingProtectedWork();
        var firstTimeProvider = CreateTimeProvider();
        var competingTimeProvider = CreateTimeProvider();
        var firstLock = new ContentionTrackingLock(
            new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey));
        var competingLock = new ContentionTrackingLock(
            new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey));
        using var firstWorker = CreateWorker(firstTimeProvider, firstLock, protectedWork);
        using var competingWorker = CreateWorker(competingTimeProvider, competingLock, protectedWork);

        await firstWorker.StartAsync();
        await firstTimeProvider.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        firstTimeProvider.Advance(TimeSpan.FromSeconds(1));
        await protectedWork.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await competingWorker.StartAsync();
        await competingTimeProvider.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        competingTimeProvider.Advance(TimeSpan.FromSeconds(1));
        await competingLock.Contended.Task.WaitAsync(TimeSpan.FromSeconds(5));

        protectedWork.InvocationCount.ShouldBe(1);
        protectedWork.MaximumConcurrent.ShouldBe(1);

        protectedWork.Release();
        await firstWorker.StopAsync();
        await competingWorker.StopAsync();
    }

    [Fact]
    public async Task Advisory_lock_is_available_after_the_holder_releases_it()
    {
        var firstWorker = new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey);
        var secondWorker = new PostgresAdvisoryLock(Database.ConnectionString, PostgresAdvisoryLock.SchedulerLockKey);

        var holder = await firstWorker.TryAcquireAsync(CancellationToken.None);
        holder.ShouldNotBeNull();
        await using (holder!)
        {
        }

        var nextLease = await secondWorker.TryAcquireAsync(CancellationToken.None);
        nextLease.ShouldNotBeNull();
        await using (nextLease!)
        {
        }
    }

    private static TrackingTimeProvider CreateTimeProvider() =>
        new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));

    private static IHost CreateWorker(
        TrackingTimeProvider timeProvider,
        IPostgresAdvisoryLock advisoryLock,
        BlockingProtectedWork protectedWork) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(timeProvider);
                services.Configure<BackgroundJobOptions>(options => options.IntervalSeconds = 1);
                services.AddSingleton<IBackgroundJob>(protectedWork);
                services.AddSingleton<IPostgresAdvisoryLock>(advisoryLock);
                services.AddHostedService<BackgroundJobWorker>();
            })
            .Build();

    private sealed class TrackingTimeProvider(FakeTimeProvider inner) : TimeProvider
    {
        public TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Advance(TimeSpan amount) => inner.Advance(amount);

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override long GetTimestamp() => inner.GetTimestamp();

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            TimerCreated.TrySetResult();
            return timer;
        }
    }

    private sealed class BlockingProtectedWork : IBackgroundJob
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximumConcurrent;
        private int _invocationCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public int MaximumConcurrent => Volatile.Read(ref _maximumConcurrent);

        public void Release() => _release.TrySetResult();

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _invocationCount);
            UpdateMaximum(active);
            Started.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int active)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maximumConcurrent);
                if (active <= current)
                    return;
            }
            while (Interlocked.CompareExchange(ref _maximumConcurrent, active, current) != current);
        }
    }

    private sealed class ContentionTrackingLock(IPostgresAdvisoryLock inner) : IPostgresAdvisoryLock
    {
        public TaskCompletionSource Contended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
        {
            var lease = await inner.TryAcquireAsync(cancellationToken);
            if (lease is null)
                Contended.TrySetResult();
            return lease;
        }
    }
}
