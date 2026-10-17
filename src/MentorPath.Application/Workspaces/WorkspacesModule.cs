using MentorPath.Application.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.Application.Workspaces;

public static class WorkspacesModule
{
    public static IServiceCollection AddWorkspacesModule(this IServiceCollection services) =>
        services.AddScoped<IMentorProvisioningService, MentorProvisioningService>();
}
