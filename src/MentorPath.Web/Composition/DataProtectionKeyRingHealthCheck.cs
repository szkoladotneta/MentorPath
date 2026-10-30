using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MentorPath.Web.Composition;

public sealed class DataProtectionKeyRingHealthCheck(
    IDataProtectionProvider dataProtection,
    IConfiguration configuration,
    IHostEnvironment environment) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (environment.IsProduction())
        {
            if (string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePath"]))
                return Task.FromResult(HealthCheckResult.Unhealthy("Data Protection certificate configuration is missing."));

            var blobUri = configuration["DataProtection:BlobUri"];
            if (!Uri.TryCreate(blobUri, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.Query))
                return Task.FromResult(HealthCheckResult.Unhealthy("Data Protection key-ring configuration is missing or invalid."));
        }

        var value = RandomNumberGenerator.GetBytes(32);
        var protector = dataProtection.CreateProtector("MentorPath.HealthChecks.DataProtection");
        var protectedValue = protector.Protect(value);
        var roundTrip = protector.Unprotect(protectedValue);
        var healthy = CryptographicOperations.FixedTimeEquals(value, roundTrip);
        return Task.FromResult(healthy
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Data Protection key-ring round-trip failed."));
    }
}
