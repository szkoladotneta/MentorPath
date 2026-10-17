using MentorPath.Application.Submissions.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Submissions;

public static class SubmissionsModule
{
    public static IServiceCollection AddSubmissionsModule(this IServiceCollection services) =>
        services.AddScoped<ISubmissionService, SubmissionService>();
}
