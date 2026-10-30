using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MentorPath.Application.Common.Email;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Cli;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace MentorPath.IntegrationTests.Cli;

public sealed class MigrationCliTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public void Migration_composition_registers_only_database_and_role_services()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=configuration_only",
            })
            .Build();

        services.AddMentorPathMigrationServices(configuration);

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDbContextFactory<AppDbContext>));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(RoleManager<IdentityRole<Guid>>));
        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(UserManager<AppUser>));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IDataProtectionProvider));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IHostedService));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IFileStorage));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IEmailSender<AppUser>));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IUserTwoFactorTokenProvider<AppUser>));
    }

    [Fact]
    public async Task Production_migrate_process_applies_fresh_database_and_initializes_roles_idempotently()
    {
        var databaseName = $"migration_cli_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(databaseName);
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(Database.ConnectionString)
            {
                Database = databaseName,
            }.ConnectionString;

            var before = await RunCliAsync(connectionString);
            before.ExitCode.ShouldBe(0, before.StandardOutput + before.StandardError);
            before.StandardOutput.ShouldContain("Migrations applied.");

            await using (var db = CreateDbContext(connectionString))
            {
                (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
                var roles = await db.Roles.Select(role => role.Name).ToListAsync();
                foreach (var role in AppRoles.All) roles.ShouldContain(role);
                roles.Count.ShouldBe(AppRoles.All.Length);
            }

            var second = await RunCliAsync(connectionString);
            second.ExitCode.ShouldBe(0, second.StandardOutput + second.StandardError);
            second.StandardOutput.ShouldContain("Migrations applied.");

            await using var verified = CreateDbContext(connectionString);
            var finalRoles = await verified.Roles.Select(role => role.Name).ToListAsync();
            finalRoles.Count.ShouldBe(AppRoles.All.Length);
            foreach (var role in AppRoles.All) finalRoles.ShouldContain(role);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Migration_cli_returns_configuration_and_execution_failures_without_success_or_credentials()
    {
        var missingConnection = await RunCliAsync(null);
        missingConnection.ExitCode.ShouldBe(2);
        missingConnection.StandardOutput.ShouldNotContain("Migrations applied.");

        const string password = "migration-cli-password-must-not-leak";
        const string username = "migration-cli-username-must-not-leak";
        var failedConnection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "unavailable",
            Username = username,
            Password = password,
            Timeout = 1,
            CommandTimeout = 2,
        }.ConnectionString;
        var failedMigration = await RunCliAsync(failedConnection);

        failedMigration.ExitCode.ShouldBe(1);
        failedMigration.StandardOutput.ShouldNotContain("Migrations applied.");
        (failedMigration.StandardOutput + failedMigration.StandardError).ShouldNotContain(password);
        (failedMigration.StandardOutput + failedMigration.StandardError).ShouldNotContain(username);
    }

    [Fact]
    public async Task Production_create_mentor_cli_keeps_the_certificate_requirement()
    {
        var result = await RunCliAsync(
            "Host=localhost;Database=configuration_only",
            "create-mentor",
            configureWebAdmin: true);

        result.ExitCode.ShouldBe(2);
        result.StandardError.ShouldContain("DataProtection:CertificatePath is required in Production");
        result.StandardOutput.ShouldNotContain("Mentor created.");
    }

    [Fact]
    public async Task Password_setup_token_round_trips_between_separate_production_hosts()
    {
        var directory = Directory.CreateTempSubdirectory("mentorpath-admin-token-");
        try
        {
            using var rsa = RSA.Create(2048);
            var certificateRequest = new CertificateRequest("CN=MentorPath test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var now = TimeProvider.System.GetUtcNow();
            using var certificate = certificateRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
            const string certificatePassword = "test-certificate-password";
            var certificatePath = Path.Combine(directory.FullName, "certificate.pfx");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, certificatePassword));
            var keysPath = Path.Combine(directory.FullName, "keys");
            var request = new ProvisionMentorRequest(
                $"mentor-{Guid.NewGuid():N}@example.test",
                "Test Mentor",
                "Test Workspace");

            Guid userId;
            string token;
            await using (var administrativeHost = CreateProductionHost(keysPath, certificatePath, certificatePassword))
            {
                await WebComposition.EnsureRolesAsync(administrativeHost.Services);
                var result = (await administrativeHost.Services.GetRequiredService<IMentorProvisioningService>()
                    .ProvisionMentorAsync(request, CancellationToken.None)).ShouldSucceed();
                userId = result.UserId;
                var query = QueryHelpers.ParseQuery(new Uri(result.PasswordSetupUrl).Query);
                token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(query["code"]!));
            }

            await using var webHost = CreateProductionHost(keysPath, certificatePath, certificatePassword);
            var users = webHost.Services.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            user.ShouldNotBeNull();
            var reset = await users.ResetPasswordAsync(user!, token, "Valid-password-123");
            reset.Succeeded.ShouldBeTrue(string.Join("; ", reset.Errors.Select(error => error.Description)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private WebApplication CreateProductionHost(string keysPath, string certificatePath, string certificatePassword)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = Database.ConnectionString,
            ["App:PublicBaseUrl"] = "https://mentorpath.test",
            ["Storage:Provider"] = "AzureBlob",
            ["Storage:AzureBlob:ServiceUri"] = "https://account.blob.core.windows.net",
            ["Storage:AzureBlob:ContainerName"] = "mentorpath",
            ["DataProtection:KeysPath"] = keysPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = certificatePassword,
        });
        builder.AddMentorPathWeb();
        return builder.Build();
    }

    private AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    private string MaintenanceConnectionString() =>
        new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Database = "postgres" }.ConnectionString;

    private static async Task<CliResult> RunCliAsync(
        string? connectionString,
        string command = "migrate",
        bool configureWebAdmin = false)
    {
        var workingDirectory = Directory.CreateTempSubdirectory("mentorpath-migrate-cli-");
        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(typeof(CliRunner).Assembly.Location);
            startInfo.ArgumentList.Add(command);
            foreach (var key in startInfo.Environment.Keys.ToArray())
            {
                if (key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("DataProtection__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("Storage__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("Email__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("Ai__", StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith("Mcp__", StringComparison.OrdinalIgnoreCase))
                    startInfo.Environment.Remove(key);
            }

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
            startInfo.Environment["HOME"] = workingDirectory.FullName;
            if (connectionString is not null)
                startInfo.Environment["ConnectionStrings__Default"] = connectionString;
            if (configureWebAdmin)
            {
                startInfo.Environment["App__PublicBaseUrl"] = "https://mentorpath.test";
                startInfo.Environment["Storage__Provider"] = "AzureBlob";
                startInfo.Environment["Storage__AzureBlob__ServiceUri"] = "https://account.blob.core.windows.net";
                startInfo.Environment["Storage__AzureBlob__ContainerName"] = "mentorpath";
            }

            using var process = Process.Start(startInfo)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            await Task.WhenAll(stdoutTask, stderrTask);
            return new CliResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}
