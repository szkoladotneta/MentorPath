using MentorPath.Application.Invitations.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Invitations;

public static class InvitationsModule
{
    public static IServiceCollection AddInvitationsModule(this IServiceCollection services) =>
        services.AddScoped<IInvitationService, InvitationService>();
}
