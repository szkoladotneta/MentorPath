using MentorPath.Application.AiAuthoring.Contracts;
using MentorPath.Application.AiAuthoring.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.AiAuthoring;

public static class AiAuthoringModule
{
    public static IServiceCollection AddAiAuthoringModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        var provider = configuration["Ai:Provider"] ?? "Fake";
        if (string.Equals(provider, "OpenAiCompatible", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<IExerciseDraftGenerator, OpenAiCompatibleDraftGenerator>(c => c.Timeout = Timeout.InfiniteTimeSpan);
        else
            services.AddSingleton<IExerciseDraftGenerator, FakeExerciseDraftGenerator>();
        services.AddScoped<IAiExerciseDraftService, AiExerciseDraftService>();
        return services;
    }
}
