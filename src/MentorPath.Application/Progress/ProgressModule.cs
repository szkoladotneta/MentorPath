using MentorPath.Application.Progress.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Progress;

public static class ProgressModule
{
    public static IServiceCollection AddProgressModule(this IServiceCollection services) =>
        services.AddScoped<IProgressService, ProgressService>()
            .AddScoped<IMentorInsightsService, MentorInsightsService>()
            .AddScoped<IRoadmapMapService, RoadmapMapService>();
}
