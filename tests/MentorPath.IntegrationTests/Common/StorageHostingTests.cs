using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public void Production_rejects_persisted_keys_without_certificate()
    {
        var builder = Builder();
        builder.Configuration["DataProtection:KeysPath"] = Path.GetTempPath();
        var error = Should.Throw<InvalidOperationException>(() => builder.AddMentorPathWeb());
        error.Message.ShouldBe("DataProtection:CertificatePath is required in Production to encrypt the key ring.");
    }

    [Fact]
    public void Development_allows_persisted_keys_without_certificate()
    {
        var builder = Builder("Development");
        builder.Configuration["DataProtection:KeysPath"] = Path.GetTempPath();
        Should.NotThrow(() => builder.AddMentorPathWeb());
    }

    [Fact]
    public void Production_encrypts_key_ring_and_reads_it_after_restart()
    {
        var directory = Directory.CreateTempSubdirectory("mentorpath-dp-");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MentorPath test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var now = TimeProvider.System.GetUtcNow();
            using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
            var certificatePath = Path.Combine(directory.FullName, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
            var keysPath = Path.Combine(directory.FullName, "keys");

            WebApplication CreateApp()
            {
                var builder = Builder();
                builder.Configuration["DataProtection:KeysPath"] = keysPath;
                builder.Configuration["DataProtection:CertificatePath"] = certificatePath;
                builder.AddMentorPathWeb();
                return builder.Build();
            }

            string protectedValue;
            using (var app = CreateApp())
                protectedValue = app.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("certificate-test").Protect("round-trip");

            var keyFile = Directory.GetFiles(keysPath, "key-*.xml").ShouldHaveSingleItem();
            var key = XDocument.Load(keyFile);
            key.Descendants().Where(node => node.Name.LocalName == "encryptedSecret")
                .ShouldHaveSingleItem().Attribute("decryptorType")!.Value.ShouldBe(typeof(EncryptedXmlDecryptor).AssemblyQualifiedName);
            key.Descendants(XName.Get("EncryptedKey", "http://www.w3.org/2001/04/xmlenc#")).ShouldHaveSingleItem();
            key.Descendants().Where(node => node.Name.LocalName == "masterKey").ShouldBeEmpty();

            using var restartedApp = CreateApp();
            restartedApp.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("certificate-test").Unprotect(protectedValue).ShouldBe("round-trip");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static WebApplicationBuilder Builder(string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=configuration_only",
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
        });
        return builder;
    }
}
