using Azure.Identity;
using Azure.Storage.Blobs;
using MentorPath.Application.AiAuthoring;
using MentorPath.Application.Catalog;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Common.Email;
using MentorPath.Application.Common.Markdown;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Feedback;
using MentorPath.Application.Invitations;
using MentorPath.Application.Mentorships;
using MentorPath.Application.Privacy;
using MentorPath.Application.Progress;
using MentorPath.Application.Reviews;
using MentorPath.Application.Roadmaps;
using MentorPath.Application.Submissions;
using MentorPath.Application.Workspaces;
using MentorPath.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MentorPath.Application.Common;

public static class MentorPathApplication
{
    /// <summary>Central composition of application services (owner: A1). Modules register themselves in their own *Module.cs.</summary>
    public static IServiceCollection AddMentorPathModules(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<AppOptions>(configuration.GetSection("App"));
        services.Configure<SmtpOptions>(configuration.GetSection("Email:Smtp"));
        services.AddSingleton<IEmailSender<AppUser>, SmtpEmailSender>();
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
        services.AddFileStorage(configuration);
        services.AddScoped<IOrphanedBlobCleanup, OrphanedBlobCleanup>();

        services.AddWorkspacesModule();
        services.AddMentorshipsModule();
        services.AddInvitationsModule();
        services.AddCatalogModule();
        services.AddAiAuthoringModule(configuration);
        services.AddRoadmapsModule();
        services.AddSubmissionsModule();
        services.AddReviewsModule();
        services.AddProgressModule();
        services.AddPrivacyModule();
        services.AddFeedbackModule();
        return services;
    }

    private static void AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"] ?? "AzureBlob";
        if (provider == "InMemory")
        {
            services.AddSingleton<IFileStorage, InMemoryFileStorage>();
            return;
        }

        if (provider != "AzureBlob")
            throw new InvalidOperationException("Storage:Provider must be AzureBlob or InMemory (tests only).");

        services.AddOptions<AzureBlobStorageOptions>()
            .Bind(configuration.GetSection("Storage:AzureBlob"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString)
                || (Uri.TryCreate(o.ServiceUri, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Query)),
                "Configure Storage:AzureBlob:ServiceUri (HTTPS, no SAS) or ConnectionString (local emulator).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ContainerName) && System.Text.RegularExpressions.Regex.IsMatch(o.ContainerName,
                "^[a-z0-9](?:[a-z0-9]|-(?=[a-z0-9])){1,61}[a-z0-9]$"),
                "Storage:AzureBlob:ContainerName must be a valid Azure container name.")
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;
            return string.IsNullOrWhiteSpace(o.ConnectionString)
                ? new BlobServiceClient(new Uri(o.ServiceUri), new DefaultAzureCredential())
                : new BlobServiceClient(o.ConnectionString);
        });
        services.AddSingleton(sp => sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(
            sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value.ContainerName));
        services.AddSingleton<AzureBlobFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<AzureBlobFileStorage>());
    }

    public static async Task EnsureStorageAsync(IServiceProvider services, CancellationToken ct)
    {
        if (services.GetService<AzureBlobFileStorage>() is { } blob) await blob.EnsureContainerAsync(ct);
    }
}
