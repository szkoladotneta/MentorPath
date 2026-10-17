using MentorPath.Application.Reviews.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Reviews;

public static class ReviewsModule
{
    public static IServiceCollection AddReviewsModule(this IServiceCollection services)
    {
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IReflectionService, ReflectionService>();
        return services;
    }
}
