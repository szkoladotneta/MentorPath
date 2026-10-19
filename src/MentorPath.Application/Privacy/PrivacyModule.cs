using MentorPath.Application.Privacy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Privacy;

public static class PrivacyModule
{
    public static IServiceCollection AddPrivacyModule(this IServiceCollection services) =>
        services.AddScoped<IPersonalDataService, PersonalDataService>();
}
