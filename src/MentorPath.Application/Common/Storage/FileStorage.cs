using System.Collections.Concurrent;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MentorPath.Application.Common.Storage;

public interface IFileStorage
{
    Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

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

    public async Task EnsureContainerAsync(CancellationToken ct) =>
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
}

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType)> _objects = new();

    public IReadOnlyCollection<string> Keys => _objects.Keys.ToArray();

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _objects[objectKey] = (ms.ToArray(), contentType);
    }

    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) =>
        _objects.TryGetValue(objectKey, out var o)
            ? Task.FromResult<Stream>(new MemoryStream(o.Data, writable: false))
            : throw new FileNotFoundException("Object not found.", objectKey);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        _objects.TryRemove(objectKey, out _);
        return Task.CompletedTask;
    }
}
