using MentorPath.Web.BackgroundJobs;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace MentorPath.IntegrationTests.Common;

public sealed class BackgroundJobHostingTests
{
    [Fact]
    public async Task Web_host_registers_and_lifetimes_the_worker()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        var timeProvider = new TrackingTimeProvider(
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));
        var job = new BlockingBackgroundJob();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=background_jobs",
            ["Storage:Provider"] = "InMemory",
            ["Ai:Provider"] = "Fake",
        });
        builder.AddMentorPathWeb();
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(timeProvider);
        builder.Services.Configure<BackgroundJobOptions>(options => options.IntervalSeconds = 1);
        builder.Services.AddSingleton<IBackgroundJob>(job);
        builder.Services.RemoveAll<IPostgresAdvisoryLock>();
        builder.Services.AddSingleton<IPostgresAdvisoryLock, AvailableAdvisoryLock>();

        await using var app = builder.Build();

        app.Services.GetServices<IHostedService>().ShouldContain(service => service is BackgroundJobWorker);
        await app.StartAsync();
        await timeProvider.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        job.InvocationCount.ShouldBe(0);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await job.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        job.InvocationCount.ShouldBe(1);

        await app.StopAsync();
        await job.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Worker_continues_with_other_jobs_after_a_job_fails()
    {
        var timeProvider = new TrackingTimeProvider(
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)));
        var succeedingJob = new CompletingBackgroundJob();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(timeProvider);
                services.Configure<BackgroundJobOptions>(options => options.IntervalSeconds = 1);
                services.AddSingleton<IBackgroundJob, FailingBackgroundJob>();
                services.AddSingleton<IBackgroundJob>(succeedingJob);
                services.AddSingleton<IPostgresAdvisoryLock, AvailableAdvisoryLock>();
                services.AddHostedService<BackgroundJobWorker>();
            })
            .Build();

        await host.StartAsync();
        await timeProvider.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await succeedingJob.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await succeedingJob.SecondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await host.StopAsync();
    }

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

    private sealed class BlockingBackgroundJob : IBackgroundJob
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocationCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            Started.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class AvailableAdvisoryLock : IPostgresAdvisoryLock
    {
        public ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(new NoopLease());
    }

    private sealed class FailingBackgroundJob : IBackgroundJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Expected test failure."));
    }

    private sealed class CompletingBackgroundJob : IBackgroundJob
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocationCount;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
                Completed.TrySetResult();
            else
                SecondCompleted.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
