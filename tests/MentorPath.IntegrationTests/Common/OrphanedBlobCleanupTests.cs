using System.Runtime.CompilerServices;
using MentorPath.Application.Common.Storage;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.BackgroundJobs;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace MentorPath.IntegrationTests.Common;

public sealed class OrphanedBlobCleanupTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task RunOnce_deletes_only_unreferenced_blobs_at_least_24_hours_old()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);

        const string staleOrphan = "ws/orphan/stale";
        const string staleReference = "ws/reference/stale";
        const string recentOrphan = "ws/orphan/recent";
        await SaveAsync(App.Storage, staleOrphan);
        await SaveAsync(App.Storage, staleReference);
        await App.Db(async context =>
        {
            context.SubmissionAttachments.Add(new SubmissionAttachment
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = mentor.WorkspaceId,
                SubmissionId = submission.SubmissionId,
                ObjectKey = staleReference,
                OriginalFileName = "attachment.txt",
                ContentType = "text/plain",
                SizeBytes = 1,
                CreatedAtUtc = App.Clock.GetUtcNow().UtcDateTime,
            });
            await context.SaveChangesAsync();
            return true;
        });

        App.Clock.Advance(TimeSpan.FromHours(23));
        await SaveAsync(App.Storage, recentOrphan);
        App.Clock.Advance(TimeSpan.FromHours(1));

        var deletedCount = await App.Get<IOrphanedBlobCleanup>().RunOnceAsync(CancellationToken.None);

        deletedCount.ShouldBe(1);
        App.Storage.Keys.ShouldContain(staleReference);
        App.Storage.Keys.ShouldContain(recentOrphan);
        App.Storage.Keys.ShouldNotContain(staleOrphan);
    }

    [Fact]
    public async Task RunOnce_does_not_delete_candidates_when_listing_fails()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var storage = new InMemoryFileStorage(clock);
        const string key = "ws/orphan/listing-failure";
        await SaveAsync(storage, key);
        clock.Advance(TimeSpan.FromHours(25));
        await using var app = await CreateAppAsync(clock, new ListingFailureStorage(storage));

        await Should.ThrowAsync<IOException>(() => app.Get<IOrphanedBlobCleanup>().RunOnceAsync(CancellationToken.None));

        storage.Keys.ShouldContain(key);
    }

    [Fact]
    public async Task RunOnce_keeps_a_blob_referenced_during_inventory()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var storage = new InMemoryFileStorage(clock);
        Func<Task> addReference = () => Task.CompletedTask;
        var listingStorage = new ReferenceOnListingStorage(storage, () => addReference());
        await using var app = await CreateAppAsync(clock, listingStorage);

        var mentor = await app.Data.MentorAsync();
        var mentee = await app.Data.MenteeAsync(mentor);
        var assignment = await app.Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await app.Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        const string key = "ws/reference/added-during-listing";
        await SaveAsync(storage, key);
        clock.Advance(TimeSpan.FromHours(25));
        addReference = () => AddAttachmentAsync(app, mentor.WorkspaceId, submission.SubmissionId, key, clock.GetUtcNow().UtcDateTime);

        (await app.Get<IOrphanedBlobCleanup>().RunOnceAsync(CancellationToken.None)).ShouldBe(0);

        storage.Keys.ShouldContain(key);
        (await app.Db(context => context.SubmissionAttachments.AnyAsync(attachment => attachment.ObjectKey == key))).ShouldBeTrue();
    }

    [Fact]
    public async Task RunOnce_propagates_delete_errors_and_can_be_retried()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var storage = new InMemoryFileStorage(clock);
        const string key = "ws/orphan/delete-retry";
        await SaveAsync(storage, key);
        clock.Advance(TimeSpan.FromHours(25));
        await using var app = await CreateAppAsync(clock, new FailOnceDeleteStorage(storage));
        var cleanup = app.Get<IOrphanedBlobCleanup>();

        await Should.ThrowAsync<IOException>(() => cleanup.RunOnceAsync(CancellationToken.None));
        storage.Keys.ShouldContain(key);

        (await cleanup.RunOnceAsync(CancellationToken.None)).ShouldBe(1);
        storage.Keys.ShouldNotContain(key);
    }

    [Fact]
    public async Task RunOnce_does_not_delete_when_database_access_fails()
    {
        var factory = new FailingDbContextFactory(Database.ConnectionString);
        await using var app = await TestApp.CreateAsync(Database, overrides: services =>
            services.AddSingleton<IDbContextFactory<AppDbContext>>(factory));
        const string key = "ws/orphan/database-failure";
        await SaveAsync(app.Storage, key);
        app.Clock.Advance(TimeSpan.FromHours(25));
        factory.Fail = true;

        await Should.ThrowAsync<IOException>(() => app.Get<IOrphanedBlobCleanup>().RunOnceAsync(CancellationToken.None));

        app.Storage.Keys.ShouldContain(key);
    }

    [Fact]
    public async Task RunOnce_does_not_delete_when_cancelled_before_database_read()
    {
        var key = "ws/orphan/cancelled";
        await SaveAsync(App.Storage, key);
        App.Clock.Advance(TimeSpan.FromHours(25));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => App.Get<IOrphanedBlobCleanup>().RunOnceAsync(cts.Token));

        App.Storage.Keys.ShouldContain(key);
    }

    [Fact]
    public void Web_host_registers_cleanup_with_the_shared_background_job_worker()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=background_jobs",
            ["Storage:Provider"] = "InMemory",
            ["Ai:Provider"] = "Fake",
        });

        builder.AddMentorPathWeb();
        using var services = builder.Services.BuildServiceProvider();

        services.GetServices<IBackgroundJob>()
            .ShouldContain(job => job.GetType().Name == "OrphanedBlobCleanupJob");
        services.GetServices<IHostedService>()
            .ShouldContain(service => service.GetType().Name == "BackgroundJobWorker");
    }

    private async Task<TestApp> CreateAppAsync(TimeProvider clock, IFileStorage storage) =>
        await TestApp.CreateAsync(Database, overrides: services =>
        {
            services.AddSingleton(clock);
            services.AddSingleton(storage);
        });

    private static Task SaveAsync(IFileStorage storage, string key) =>
        storage.SaveAsync(key, new MemoryStream("x"u8.ToArray()), "text/plain", CancellationToken.None);

    private static async Task AddAttachmentAsync(TestApp app, Guid workspaceId, Guid submissionId, string objectKey, DateTime createdAtUtc)
    {
        await app.Db(async context =>
        {
            context.SubmissionAttachments.Add(new SubmissionAttachment
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                SubmissionId = submissionId,
                ObjectKey = objectKey,
                OriginalFileName = "attachment.txt",
                ContentType = "text/plain",
                SizeBytes = 1,
                CreatedAtUtc = createdAtUtc,
            });
            await context.SaveChangesAsync();
            return true;
        });
    }

    private sealed class ListingFailureStorage(InMemoryFileStorage inner) : IFileStorage
    {
        public Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct) =>
            inner.SaveAsync(objectKey, content, contentType, ct);

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) => inner.OpenReadAsync(objectKey, ct);

        public Task DeleteAsync(string objectKey, CancellationToken ct) => inner.DeleteAsync(objectKey, ct);

        public async IAsyncEnumerable<StoredBlobInfo> ListAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var blob in inner.ListAsync(ct))
                yield return blob;
            throw new IOException("Listing failed after a partial result.");
        }
    }

    private sealed class FailOnceDeleteStorage(InMemoryFileStorage inner) : IFileStorage
    {
        private bool _shouldFail = true;

        public Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct) =>
            inner.SaveAsync(objectKey, content, contentType, ct);

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) => inner.OpenReadAsync(objectKey, ct);

        public Task DeleteAsync(string objectKey, CancellationToken ct)
        {
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new IOException("Delete failed.");
            }

            return inner.DeleteAsync(objectKey, ct);
        }

        public IAsyncEnumerable<StoredBlobInfo> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
    }

    private sealed class ReferenceOnListingStorage(InMemoryFileStorage inner, Func<Task> addReference) : IFileStorage
    {
        public Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct) =>
            inner.SaveAsync(objectKey, content, contentType, ct);

        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) => inner.OpenReadAsync(objectKey, ct);

        public Task DeleteAsync(string objectKey, CancellationToken ct) => inner.DeleteAsync(objectKey, ct);

        public async IAsyncEnumerable<StoredBlobInfo> ListAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await addReference();
            await foreach (var blob in inner.ListAsync(ct))
                yield return blob;
        }
    }

    private sealed class FailingDbContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options =
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;

        public bool Fail { get; set; }

        public AppDbContext CreateDbContext()
        {
            if (Fail) throw new IOException("Database access failed.");
            return new AppDbContext(_options);
        }

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
