using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Mentorships;

public static class MentorshipsModule
{
    public static IServiceCollection AddMentorshipsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<McpReviewOptions>()
            .Bind(configuration.GetSection(McpReviewOptions.SectionName))
            .Validate(options => !options.Enabled
                || (!string.IsNullOrWhiteSpace(options.ProviderNotice) && options.ProviderNotice.Length <= InputLimits.Markdown),
                "Mcp:Reviews:ProviderNotice must contain a provider disclosure of at most 20000 characters when reviews are enabled.")
            .ValidateOnStart();
        services.AddScoped<IMentorshipReviewConsentService, MentorshipReviewConsentService>();
        return services.AddScoped<IMentorshipService, MentorshipService>();
    }
}
