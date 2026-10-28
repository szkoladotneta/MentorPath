using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MentorPath.Application.Common.Storage;

public interface IFileStorage
{
    Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
    IAsyncEnumerable<StoredBlobInfo> ListAsync(CancellationToken ct);
}

public sealed record StoredBlobInfo(string ObjectKey, DateTimeOffset? LastModifiedUtc);

public sealed class AzureBlobStorageOptions
{
    public string ServiceUri { get; set; } = "";
    public string ContainerName { get; set; } = "mentorpath";
    public string ConnectionString { get; set; } = "";
}

internal sealed class AzureBlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        await container.GetBlobClient(objectKey).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            var response = await container.GetBlobClient(objectKey).DownloadStreamingAsync(cancellationToken: ct);
            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == BlobErrorCode.BlobNotFound.ToString())
        {
            throw new FileNotFoundException("Object not found.", objectKey, ex);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await container.GetBlobClient(objectKey).DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == BlobErrorCode.BlobNotFound.ToString())
        {
            // Retried upload compensation and anonymization may already have removed this blob.
        }
    }

    public async IAsyncEnumerable<StoredBlobInfo> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var blob in container.GetBlobsAsync(cancellationToken: ct))
            yield return new StoredBlobInfo(blob.Name, blob.Properties.LastModified);
    }

    public async Task EnsureContainerAsync(CancellationToken ct) =>
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
}

public sealed class InMemoryFileStorage(TimeProvider? timeProvider = null) : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType, DateTimeOffset LastModifiedUtc)> _objects = new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public IReadOnlyCollection<string> Keys => _objects.Keys.ToArray();

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _objects[objectKey] = (ms.ToArray(), contentType, _timeProvider.GetUtcNow());
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) =>
        _objects.TryGetValue(objectKey, out var o)
            ? Task.FromResult<Stream>(new MemoryStream(o.Data, writable: false))
            : throw new FileNotFoundException("Object not found.", objectKey);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredBlobInfo> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (objectKey, value) in _objects.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            yield return new StoredBlobInfo(objectKey, value.LastModifiedUtc);
        }

        await Task.CompletedTask;
    }
}
