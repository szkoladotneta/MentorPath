using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MentorPath.UnitTests.Common;

public sealed class FileStorageTests
{
    [Fact]
    public void Missing_configuration_fails_instead_of_using_memory()
    {
        using var services = Services([]);
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IFileStorage>());
    }

    [Theory]
    [InlineData("http://account.blob.core.windows.net")]
    [InlineData("not-a-uri")]
    [InlineData("https://account.blob.core.windows.net?sig=not-allowed")]
    public void Managed_identity_requires_an_https_endpoint_without_sas(string serviceUri)
    {
        using var services = Services(new() { ["Storage:AzureBlob:ServiceUri"] = serviceUri });
        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IFileStorage>());
    }

    [Fact]
    public void AzureBlob_is_the_default_and_uses_the_configured_container()
    {
        using var services = Services(new()
        {
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ContainerName"] = "attachments",
        });
        services.GetRequiredService<IFileStorage>().ShouldBeOfType<AzureBlobFileStorage>();
        services.GetRequiredService<BlobContainerClient>().Uri.AbsoluteUri
            .ShouldBe("https://account.blob.core.windows.net/attachments");
    }

    [Fact]
    public void Memory_storage_requires_explicit_selection()
    {
        using var services = Services(new() { ["Storage:Provider"] = "InMemory" });
        services.GetRequiredService<IFileStorage>().ShouldBeOfType<InMemoryFileStorage>();
    }

    [Fact]
    public void Unknown_provider_fails_explicitly()
    {
        Should.Throw<InvalidOperationException>(() => Services(new() { ["Storage:Provider"] = "S3" }));
    }

    [Theory]
    [InlineData(404, "BlobNotFound", true)]
    [InlineData(404, "ContainerNotFound", false)]
    [InlineData(403, "AuthorizationPermissionMismatch", false)]
    [InlineData(500, "InternalError", false)]
    public async Task Read_only_maps_a_missing_blob_to_file_not_found(int status, string code, bool missing)
    {
        var error = new RequestFailedException(status, "Storage request failed.", code, null);
        var storage = new AzureBlobFileStorage(new FailingContainer(error));
        if (missing)
        {
            var result = await Should.ThrowAsync<FileNotFoundException>(() => storage.OpenReadAsync("ws/test/sub/file", CancellationToken.None));
            result.InnerException.ShouldBeSameAs(error);
        }
        else
        {
            var result = await Should.ThrowAsync<RequestFailedException>(() => storage.OpenReadAsync("ws/test/sub/file", CancellationToken.None));
            result.ShouldBeSameAs(error);
        }
    }

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMentorPathModules(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FailingContainer(RequestFailedException error) : BlobContainerClient
    {
        public override BlobClient GetBlobClient(string blobName) => new FailingBlob(error);
    }

    private sealed class FailingBlob(RequestFailedException error) : BlobClient
    {
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromException<Response<BlobDownloadStreamingResult>>(error);
    }
}
