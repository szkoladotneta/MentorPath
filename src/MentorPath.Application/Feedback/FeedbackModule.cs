using MentorPath.Application.Feedback.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Feedback;

public static class FeedbackModule
{
    public static IServiceCollection AddFeedbackModule(this IServiceCollection services)
    {
        services.AddScoped<IProductFeedbackService, ProductFeedbackService>();
        services.AddScoped<IProductFeedbackOperatorService, ProductFeedbackOperatorService>();
        return services;
    }
}
