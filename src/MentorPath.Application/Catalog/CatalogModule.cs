using MentorPath.Application.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ITopicService, TopicService>();
        services.AddScoped<ExerciseAuthoringService>();
        services.AddScoped<IExerciseAuthoringService>(sp => sp.GetRequiredService<ExerciseAuthoringService>());
        return services;
    }
}
