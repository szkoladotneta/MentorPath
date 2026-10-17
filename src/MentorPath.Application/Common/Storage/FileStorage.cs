using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Common.Storage;

public interface IFileStorage
{
    Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public sealed class S3StorageOptions
{
    public string ServiceUrl { get; set; } = "";
    public string BucketName { get; set; } = "mentorpath";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool ForcePathStyle { get; set; } = true;
    public string Region { get; set; } = "us-east-1";
}

internal sealed class S3FileStorage(IAmazonS3 s3, IOptions<S3StorageOptions> options) : IFileStorage
{
    private readonly string _bucket = options.Value.BucketName;

    public async Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct)
    {
        // S3 requires a known length; buffer non-seekable streams (max 10 MB by contract).
        Stream body = content;
        if (!content.CanSeek)
        {
            var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            body = buffer;
        }

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = body,
            ContentType = contentType,
            AutoCloseStream = false,
        }, ct);
    }

    public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            var response = await s3.GetObjectAsync(_bucket, objectKey, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Object not found.", objectKey, ex);
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct) =>
        await s3.DeleteObjectAsync(_bucket, objectKey, ct);

    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }
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
