using MentorPath.Application.Mentorships.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Mentorships;

public static class MentorshipsModule
{
    public static IServiceCollection AddMentorshipsModule(this IServiceCollection services) =>
        services.AddScoped<IMentorshipService, MentorshipService>();
}
