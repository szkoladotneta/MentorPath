using MentorPath.Application.Common;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.Web.Composition;
using MentorPath.Web.Mcp;
using Microsoft.AspNetCore.Identity;

namespace MentorPath.Web.Cli;

/// <summary>
/// Runs the MentorPath MCP server over stdio for local agents (Claude Code, VS Code), as the mentor given in <c>--actor</c>.
/// Development only (D-64): the process has the mentor's full rights, like any CLI run on the developer's machine.
/// </summary>
internal static class McpStdioCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CliRunner.ParseOptions(args);
        if (!options.TryGetValue("actor", out var actorEmail) || !actorEmail.Contains('@'))
        {
            Console.Error.WriteLine("Usage: mcp-stdio --actor <mentor e-mail>");
            return 2;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory, // appsettings*.json next to the binary, whatever the client's working directory is
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production,
        });
        if (!builder.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("mcp-stdio runs only in Development (ASPNETCORE_ENVIRONMENT=Development). Elsewhere use the HTTP endpoint with a token from mcp-token.");
            return 2;
        }

        // stdout is the JSON-RPC channel: every log line must go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        builder.Configuration["Storage:EnsureContainerOnStartup"] = "false";
        var services = builder.Services;
        services.AddMentorPathData(builder.Configuration);
        services.AddIdentityCore<AppUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<AppDbContext>();
        services.AddMentorPathModules(builder.Configuration);
        services.AddSingleton<McpActorOptions>();
        services.AddScoped<IActorContext, ActorContext>();
        services.AddMcpServer(McpComposition.ConfigureServer).WithStdioServerTransport().WithMentorPathCapabilities();

        using var host = builder.Build();
        var actor = host.Services.GetRequiredService<McpActorOptions>();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(actorEmail.Trim());
            if (user is null || !await users.IsInRoleAsync(user, AppRoles.Mentor)) // the same rule as mcp-token: only a mentor can be the actor
            {
                Console.Error.WriteLine("Actor must be an existing mentor account. Run seed-demo or pass a mentor's e-mail.");
                return 1;
            }
            actor.FixedActorUserId = user.Id;
        }

        await host.RunAsync();
        return 0;
    }
}
