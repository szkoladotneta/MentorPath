using MentorPath.Application.Common;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MentorPath.IntegrationTests.Reviews;

public sealed class McpReviewConcurrencyTests(PostgresFixture db) : ReviewFeatureTestBase(db)
{
    [Fact]
    public async Task Revoke_waits_for_in_flight_attachment_read_then_denies_every_later_operation()
    {
        var attachment = await PracticalAsync();
        var item = attachment.Review;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new ReadOverrideStorage(App.Storage, async (key, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return await App.Storage.OpenReadAsync(key, ct);
        });
        await using var app = await TestApp.CreateAsync(Database, EnabledSettings,
            services => services.AddSingleton<IFileStorage>(storage));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var reading = app.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId,
            item.Mentee.MentorshipId, item.SubmissionId, attachment.Attachment.AttachmentId, 0, 100, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var consent = (await App.Get<IMentorshipReviewConsentService>()
            .GetAsync(item.Mentee.UserId, item.Mentee.MentorshipId, timeout.Token)).ShouldSucceed();
        var revoking = App.Get<IMentorshipReviewConsentService>().RevokeAsync(item.Mentee.UserId,
            new(item.Mentee.MentorshipId, consent.Version!.Value), timeout.Token);
        try
        {
            // A second connection can read consent, but cannot revoke while the read holds the mentorship row.
            (await App.Db(d => d.MentorshipReviewConsents.SingleAsync(c => c.MentorshipId == item.Mentee.MentorshipId)))
                .RevokedAtUtc.ShouldBeNull();
            revoking.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        (await reading).ShouldSucceed().Content.ShouldBe(attachment.Bytes.Take(100).ToArray());
        (await revoking).ShouldSucceed();
        (await app.Get<IMcpReviewContextService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, default)).ShouldFail(ErrorKind.Conflict, "mcp_review.consent_required");
        (await app.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            new(item.SubmissionId, Complete(item), null), default)).ShouldFail(ErrorKind.Conflict, "mcp_review.consent_required");
    }

    [Fact]
    public async Task MCP_gate_observes_runtime_option_changes_without_restarting_or_recreating_services()
    {
        var item = await PendingAsync();
        var context = App.Get<IMcpReviewContextService>();
        var draft = App.Get<IMcpReviewDraftService>();
        (await context.GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldSucceed();
        var cache = App.Get<IOptionsMonitorCache<McpReviewOptions>>();
        cache.TryRemove(Options.DefaultName);
        cache.TryAdd(Options.DefaultName, new McpReviewOptions { Enabled = false, ProviderNotice = EnabledSettings["Mcp:Reviews:ProviderNotice"]! });
        (await context.GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default))
            .ShouldFail(ErrorKind.Unavailable, "mcp_review.disabled");
        (await draft.SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId, new(item.SubmissionId, Complete(item), null), default))
            .ShouldFail(ErrorKind.Unavailable, "mcp_review.disabled");
        cache.TryRemove(Options.DefaultName);
        cache.TryAdd(Options.DefaultName, new McpReviewOptions { Enabled = true, ProviderNotice = "Nowy dostawca." });
        (await context.GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default))
            .ShouldFail(ErrorKind.Conflict, "mcp_review.consent_required");
    }

    [Fact]
    public async Task Save_racing_publication_with_the_same_version_has_only_a_serialized_outcome()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        var save = App.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            new(item.SubmissionId, Complete(item) with { StrengthsMarkdown = "Nowa wersja" }, draft.Version), default);
        var publish = App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, draft.Version, default);
        await Task.WhenAll(save, publish);
        var saved = await save;
        var published = await publish;
        if (published.IsSuccess)
        {
            saved.ShouldFail(ErrorKind.Conflict, "review.not_pending");
            (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
            (await App.Db(d => d.MentorFeedback.SingleAsync(f => f.SubmissionId == item.SubmissionId)))
                .StrengthsMarkdown.ShouldBe("Mocne strony");
        }
        else
        {
            saved.ShouldSucceed().Version.ShouldBe(draft.Version + 1);
            published.ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
            (await App.Db(d => d.MentorFeedback.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Nonseekable_attachment_stream_supports_ranges_and_is_disposed_after_copying_bytes()
    {
        var attachment = await PracticalAsync();
        var stream = new NonseekableStream(attachment.Bytes);
        var storage = new ReadOverrideStorage(App.Storage, (_, _) => Task.FromResult<Stream>(stream));
        await using var app = await TestApp.CreateAsync(Database, EnabledSettings,
            services => services.AddSingleton<IFileStorage>(storage));
        var item = attachment.Review;
        var result = (await app.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId,
            item.Mentee.MentorshipId, item.SubmissionId, attachment.Attachment.AttachmentId, 90_000, 20_000, default)).ShouldSucceed();
        result.Content.ShouldBe(attachment.Bytes.Skip(90_000).ToArray());
        result.NextOffset.ShouldBeNull();
        stream.Disposed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Attachment_storage_failures_are_explicit_and_never_return_partial_content(bool missing)
    {
        var attachment = await PracticalAsync();
        var storage = new ReadOverrideStorage(App.Storage, (_, _) =>
        {
            if (missing) throw new FileNotFoundException();
            throw new IOException("Storage unavailable.");
        });
        await using var app = await TestApp.CreateAsync(Database, EnabledSettings,
            services => services.AddSingleton<IFileStorage>(storage));
        var item = attachment.Review;
        var result = await app.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId,
            item.Mentee.MentorshipId, item.SubmissionId, attachment.Attachment.AttachmentId, 0, 100, default);
        if (missing) result.ShouldBeNotFound();
        else result.ShouldFail(ErrorKind.Unavailable, "submission.storage_unavailable");
    }

    [Fact]
    public async Task Truncated_attachment_is_unavailable_not_a_successful_short_chunk()
    {
        var attachment = await PracticalAsync();
        var storage = new ReadOverrideStorage(App.Storage, (_, _) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        await using var app = await TestApp.CreateAsync(Database, EnabledSettings,
            services => services.AddSingleton<IFileStorage>(storage));
        var item = attachment.Review;
        (await app.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId,
            item.Mentee.MentorshipId, item.SubmissionId, attachment.Attachment.AttachmentId, 0, 100, default))
            .ShouldFail(ErrorKind.Unavailable, "submission.storage_unavailable");
    }

    private sealed class ReadOverrideStorage(IFileStorage inner, Func<string, CancellationToken, Task<Stream>> read) : IFileStorage
    {
        public Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct) =>
            inner.SaveAsync(objectKey, content, contentType, ct);
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) => read(objectKey, ct);
        public Task DeleteAsync(string objectKey, CancellationToken ct) => inner.DeleteAsync(objectKey, ct);
        public IAsyncEnumerable<StoredBlobInfo> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
    }

    private sealed class NonseekableStream(byte[] content) : MemoryStream(content, writable: false)
    {
        public bool Disposed { get; private set; }
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
