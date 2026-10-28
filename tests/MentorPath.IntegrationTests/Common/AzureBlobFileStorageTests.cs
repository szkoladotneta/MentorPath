using Azure;
using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MentorPath.Application.Common.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.IntegrationTests.Common;

public sealed class AzureBlobFileStorageTests : IAsyncLifetime
{
    private const string AccountKey = "bWVudG9ycGF0aC1sb2NhbC1kZXYta2V5LW9ubHk=";
    private readonly IContainer _emulator = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithEnvironment("AZURITE_ACCOUNTS", $"mentorpath:{AccountKey}")
        .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck", "--silent")
        .WithPortBinding(10000, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Azurite Blob service successfully listens",
            o => o.WithTimeout(TimeSpan.FromSeconds(60))))
        .Build();

    private BlobContainerClient _container = null!;
    private AzureBlobFileStorage _storage = null!;

    public async Task InitializeAsync()
    {
        await _emulator.StartAsync();
        var connection = $"DefaultEndpointsProtocol=http;AccountName=mentorpath;AccountKey={AccountKey};BlobEndpoint=http://{_emulator.Hostname}:{_emulator.GetMappedPublicPort(10000)}/mentorpath;";
        _container = new BlobContainerClient(connection, "attachments");
        _storage = new AzureBlobFileStorage(_container);
        await _storage.EnsureContainerAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_read_and_delete_preserve_bytes_and_content_type(bool nonSeekable)
    {
        var bytes = "%PDF-1.7\nattachment"u8.ToArray();
        using var content = new MemoryStream(bytes);
        using var stream = nonSeekable ? new NonSeekableStream(content) : (Stream)content;
        const string key = "ws/00000000000000000000000000000001/sub/test";
        await _storage.SaveAsync(key, stream, "application/pdf", CancellationToken.None);
        content.CanRead.ShouldBeTrue();
        (await _container.GetBlobClient(key).GetPropertiesAsync()).Value.ContentType.ShouldBe("application/pdf");
        await using (var read = await _storage.OpenReadAsync(key, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().ShouldBe(bytes);
        }
        await _container.GetBlobClient(key).CreateSnapshotAsync();
        await _storage.DeleteAsync(key, CancellationToken.None);
        await _storage.DeleteAsync(key, CancellationToken.None);
        await Should.ThrowAsync<FileNotFoundException>(() => _storage.OpenReadAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task List_returns_current_blob_keys_and_last_modified_times()
    {
        const string key = "ws/00000000000000000000000000000001/sub/listed";
        await _storage.SaveAsync(key, new MemoryStream("attachment"u8.ToArray()), "text/plain", CancellationToken.None);

        var listed = new List<StoredBlobInfo>();
        await foreach (var blob in _storage.ListAsync(CancellationToken.None))
            listed.Add(blob);

        var result = listed.Single(blob => blob.ObjectKey == key);
        result.LastModifiedUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Container_is_private_and_creation_is_idempotent()
    {
        await _storage.EnsureContainerAsync(CancellationToken.None);
        (await _container.GetAccessPolicyAsync()).Value.BlobPublicAccess.ShouldBe(Azure.Storage.Blobs.Models.PublicAccessType.None);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => _storage.OpenReadAsync("missing", ct.Token));
    }

    [Fact]
    public async Task Missing_container_is_an_infrastructure_error()
    {
        await _container.DeleteAsync();
        var error = await Should.ThrowAsync<RequestFailedException>(() => _storage.OpenReadAsync("missing", CancellationToken.None));
        error.ErrorCode.ShouldBe("ContainerNotFound");
        var deleteError = await Should.ThrowAsync<RequestFailedException>(() => _storage.DeleteAsync("missing", CancellationToken.None));
        deleteError.ErrorCode.ShouldBe("ContainerNotFound");
    }

    [Fact]
    public void Independent_hosts_share_identity_keys_through_blob_storage()
    {
        using var firstHost = KeyServices();
        var protectedValue = firstHost.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("reset-link-test").Protect("test-value");
        using var secondHost = KeyServices();
        secondHost.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("reset-link-test").Unprotect(protectedValue).ShouldBe("test-value");
    }

    private ServiceProvider KeyServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().SetApplicationName("MentorPath")
            .PersistKeysToAzureBlobStorage(_container.GetBlobClient("keyring.xml"));
        return services.BuildServiceProvider();
    }

    public Task DisposeAsync() => _emulator.DisposeAsync().AsTask();

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
