using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace MentorPath.IntegrationTests.Common;

public sealed class StorageHostingTests
{
    [Fact]
    public void Production_rejects_in_memory_storage()
    {
        var builder = Builder();
        builder.Configuration["Storage:Provider"] = "InMemory";
        var error = Should.Throw<InvalidOperationException>(() => builder.AddMentorPathWeb());
        error.Message.ShouldBe("InMemory storage is not allowed in Production.");
    }

    [Theory]
    [InlineData("http://account.blob.core.windows.net/keys/ring.xml")]
    [InlineData("https://account.blob.core.windows.net/keys/ring.xml?sig=not-allowed")]
    [InlineData("invalid")]
    public void Identity_key_blob_requires_https_without_sas(string uri)
    {
        var builder = Builder();
        builder.Configuration["DataProtection:BlobUri"] = uri;
        var error = Should.Throw<InvalidOperationException>(() => builder.AddMentorPathWeb());
        error.Message.ShouldBe("DataProtection:BlobUri must be an HTTPS blob URI without a SAS token.");
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=configuration_only",
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
        });
        return builder;
    }
}
