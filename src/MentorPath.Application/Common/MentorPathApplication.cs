using Amazon.Runtime;
using Amazon.S3;
using MentorPath.Application.AiAuthoring;
using MentorPath.Application.Catalog;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Common.Email;
using MentorPath.Application.Common.Markdown;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Invitations;
using MentorPath.Application.Mentorships;
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

        services.AddWorkspacesModule();
        services.AddMentorshipsModule();
        services.AddInvitationsModule();
        services.AddCatalogModule();
        services.AddAiAuthoringModule(configuration);
        services.AddRoadmapsModule();
        services.AddSubmissionsModule();
        services.AddReviewsModule();
        services.AddProgressModule();
        return services;
    }

    private static void AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage:S3");
        services.Configure<S3StorageOptions>(section);
        if (string.IsNullOrWhiteSpace(section["ServiceUrl"]))
        {
            services.AddSingleton<IFileStorage, InMemoryFileStorage>();
            return;
        }

        services.AddSingleton<IAmazonS3>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;
            var config = new AmazonS3Config
            {
                ServiceURL = o.ServiceUrl,
                ForcePathStyle = o.ForcePathStyle,
                AuthenticationRegion = o.Region,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };
            return new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), config);
        });
        services.AddSingleton<S3FileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
    }

    public static async Task EnsureStorageAsync(IServiceProvider services, CancellationToken ct)
    {
        if (services.GetService<S3FileStorage>() is { } s3) await s3.EnsureBucketAsync(ct);
    }
}
