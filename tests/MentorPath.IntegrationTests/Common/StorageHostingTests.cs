using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("keys")]
    public void Production_rejects_missing_certificate_with_or_without_explicit_store(string? keysPath)
    {
        var builder = Builder();
        builder.Configuration["DataProtection:KeysPath"] = keysPath;
        var error = Should.Throw<InvalidOperationException>(() => builder.AddMentorPathWeb());
        error.Message.ShouldBe("DataProtection:CertificatePath is required in Production to encrypt the key ring.");
    }

    [Fact]
    public void Production_rejects_an_unusable_certificate_instead_of_falling_back()
    {
        var directory = Directory.CreateTempSubdirectory("mentorpath-invalid-dp-");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MentorPath test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var now = TimeProvider.System.GetUtcNow();
            using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
            var certificatePath = Path.Combine(directory.FullName, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "right-password"));

            var builder = Builder();
            builder.Configuration["DataProtection:CertificatePath"] = certificatePath;
            builder.Configuration["DataProtection:CertificatePassword"] = "wrong-password";

            Should.Throw<CryptographicException>(() => builder.AddMentorPathWeb());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("keys")]
    public void Development_allows_missing_certificate_with_or_without_explicit_store(string? keysPath)
    {
        var builder = Builder("Development");
        builder.Configuration["DataProtection:KeysPath"] = keysPath;
        Should.NotThrow(() => builder.AddMentorPathWeb());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Production_encrypts_key_ring_and_reads_it_after_restart_with_or_without_explicit_store(bool explicitStore)
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
                if (explicitStore)
                    builder.Configuration["DataProtection:KeysPath"] = keysPath;
                builder.Configuration["DataProtection:CertificatePath"] = certificatePath;
                builder.AddMentorPathWeb();
                if (!explicitStore)
                {
                    // Isolate the framework-selected repository without supplying a persistence setting to the application.
                    builder.Services.Configure<KeyManagementOptions>(options =>
                        options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keysPath), NullLoggerFactory.Instance));
                }
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

    [Fact]
    public void Production_reads_existing_unencrypted_key_with_the_configured_certificate()
    {
        var directory = Directory.CreateTempSubdirectory("mentorpath-legacy-dp-");
        try
        {
            var keysPath = Path.Combine(directory.FullName, "keys");
            Directory.CreateDirectory(keysPath);
            var legacyServices = new ServiceCollection();
            legacyServices.AddDataProtection()
                .SetApplicationName("MentorPath")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
            using var legacyProvider = legacyServices.BuildServiceProvider();
            var protectedValue = legacyProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("legacy-key-test").Protect("existing-key");

            var keyFile = Directory.GetFiles(keysPath, "key-*.xml").ShouldHaveSingleItem();
            XDocument.Load(keyFile).Descendants()
                .Where(node => node.Name.LocalName == "encryptedSecret").ShouldBeEmpty();

            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MentorPath test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var now = TimeProvider.System.GetUtcNow();
            using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
            var certificatePath = Path.Combine(directory.FullName, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));

            var builder = Builder();
            builder.Configuration["DataProtection:KeysPath"] = keysPath;
            builder.Configuration["DataProtection:CertificatePath"] = certificatePath;
            builder.AddMentorPathWeb();
            builder.Services.Configure<KeyManagementOptions>(options =>
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keysPath), NullLoggerFactory.Instance));
            using var app = builder.Build();

            app.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("legacy-key-test").Unprotect(protectedValue).ShouldBe("existing-key");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null, "https://account.blob.core.windows.net/identity-keys/keyring.xml", "Data Protection certificate configuration is missing.")]
    [InlineData("certificate.pfx", null, "Data Protection key-ring configuration is missing or invalid.")]
    public async Task Production_readiness_rejects_missing_or_invalid_certificate_and_ring_configuration(
        string? certificatePath,
        string? blobUri,
        string expectedDescription)
    {
        var hostBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:CertificatePath"] = certificatePath,
                ["DataProtection:BlobUri"] = blobUri,
            })
            .Build();
        using var services = new ServiceCollection().AddDataProtection().Services.BuildServiceProvider();
        var check = new DataProtectionKeyRingHealthCheck(
            services.GetRequiredService<IDataProtectionProvider>(),
            configuration,
            hostBuilder.Environment);

        var report = await check.CheckHealthAsync(new HealthCheckContext());
        report.Status.ShouldBe(HealthStatus.Unhealthy);
        report.Description.ShouldBe(expectedDescription);
    }

    [Fact]
    public async Task Production_data_protection_readiness_check_exercises_the_configured_key_ring()
    {
        var directory = Directory.CreateTempSubdirectory("mentorpath-dp-ready-");
        try
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=MentorPath test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var now = TimeProvider.System.GetUtcNow();
            using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
            var certificatePath = Path.Combine(directory.FullName, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
            var keysPath = Path.Combine(directory.FullName, "keys");

            var builder = Builder();
            builder.Configuration["DataProtection:KeysPath"] = keysPath;
            builder.Configuration["DataProtection:BlobUri"] = "https://account.blob.core.windows.net/identity-keys/keyring.xml";
            builder.Configuration["DataProtection:CertificatePath"] = certificatePath;
            builder.AddMentorPathWeb();
            builder.Services.Configure<KeyManagementOptions>(options =>
                options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(keysPath), NullLoggerFactory.Instance));
            using var app = builder.Build();

            var report = await app.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(check => check.Name == "data-protection");
            report.Status.ShouldBe(HealthStatus.Healthy);

            var keyFile = Directory.GetFiles(keysPath, "key-*.xml").ShouldHaveSingleItem();
            XDocument.Load(keyFile).Descendants().Where(node => node.Name.LocalName == "encryptedSecret")
                .ShouldHaveSingleItem().Attribute("decryptorType")!.Value.ShouldBe(typeof(EncryptedXmlDecryptor).AssemblyQualifiedName);
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
            ["DataProtection:KeysPath"] = null,
            ["DataProtection:BlobUri"] = null,
            ["DataProtection:CertificatePath"] = null,
            ["DataProtection:CertificatePassword"] = null,
        });
        return builder;
    }
}
