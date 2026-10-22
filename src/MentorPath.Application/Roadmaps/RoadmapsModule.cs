using MentorPath.Application.Roadmaps.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Roadmaps;

public static class RoadmapsModule
{
    public static IServiceCollection AddRoadmapsModule(this IServiceCollection services)
    {
        services.AddScoped<IRoadmapService, RoadmapService>();
        services.AddScoped<ITopicPreparationService, TopicPreparationService>();
        return services;
    }
}
