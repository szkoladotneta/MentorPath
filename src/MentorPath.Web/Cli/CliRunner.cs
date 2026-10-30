using System.Security.Cryptography;
using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.Web.Composition;
using MentorPath.Web.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MentorPath.Web.Cli;

/// <summary>Administrative commands executed from the same binary (D-24, D-64): migrate, create-mentor, seed-demo, export-user, anonymize-user, list-feedback, mcp-stdio, mcp-token, mcp-key.</summary>
public static class CliRunner
{
    private static readonly string[] Commands = ["migrate", "create-mentor", "seed-demo", "export-user", "anonymize-user", "list-feedback", "mcp-stdio", "mcp-token", "mcp-key"];

    public static bool IsCliCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        if (command == "mcp-stdio") return await McpStdioCommand.RunAsync(args.Skip(1).ToArray()); // generic host, no Kestrel
        if (command == "mcp-key") // needs no host and no database: prints a fresh Mcp:SigningKey value
        {
            Console.WriteLine(McpTokens.GenerateKey());
            return 0;
        }
        if (command == "migrate") return await RunMigrationsAsync(args);

        var options = ParseOptions(args.Skip(1).ToArray());
        var builder = WebApplication.CreateBuilder(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal) || a.Contains('=')).ToArray());
        builder.Configuration["Storage:EnsureContainerOnStartup"] = "false";
        WebApplication app;
        try
        {
            builder.AddMentorPathWeb();
            app = builder.Build();
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or IOException) // configuration errors: a message, not a stack trace
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return 2;
        }
        await using var _ = app;
        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        try
        {
            switch (command)
            {
                case "migrate":
                    await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                    await WebComposition.EnsureRolesAsync(sp);
                    Console.WriteLine("Migrations applied.");
                    return 0;

                case "create-mentor":
                {
                    if (!options.TryGetValue("email", out var email) || !options.TryGetValue("name", out var name) || !options.TryGetValue("workspace", out var workspace))
                    {
                        Console.Error.WriteLine("Usage: create-mentor --email <email> --name <display name> --workspace <workspace name>");
                        return 2;
                    }
                    await WebComposition.EnsureRolesAsync(sp);
                    var result = await sp.GetRequiredService<IMentorProvisioningService>()
                        .ProvisionMentorAsync(new ProvisionMentorRequest(email, name, workspace), CancellationToken.None);
                    if (!result.IsSuccess)
                    {
                        Console.Error.WriteLine($"Error: {result.Error!.Message}");
                        foreach (var f in result.Error.FieldErrors ?? new Dictionary<string, string[]>())
                            Console.Error.WriteLine($"  {f.Key}: {string.Join(" ", f.Value)}");
                        return 1;
                    }
                    Console.WriteLine($"Mentor created. UserId={result.Value!.UserId} WorkspaceId={result.Value.WorkspaceId}");
                    Console.WriteLine("One-time password setup link (share securely with the mentor):");
                    Console.WriteLine(result.Value.PasswordSetupUrl);
                    return 0;
                }

                case "seed-demo":
                    if (!app.Environment.IsDevelopment() && !options.ContainsKey("force"))
                    {
                        Console.Error.WriteLine("seed-demo runs only in Development. Use --force to override (never on production data).");
                        return 2;
                    }
                    await sp.GetRequiredService<AppDbContext>().Database.MigrateAsync();
                    await WebComposition.EnsureRolesAsync(sp);
                    var password = options.TryGetValue("password", out var p) ? p : "Demo-password-2026";
                    return await DemoSeed.RunAsync(sp, password, Console.Out);

                case "export-user":
                    return await PrivacyCommands.ExportAsync(sp, options, Console.Out, Console.Error, CancellationToken.None);

                case "anonymize-user":
                    return await PrivacyCommands.AnonymizeAsync(sp, options, Console.Out, Console.Error, CancellationToken.None);

                case "list-feedback":
                    return await FeedbackCommands.ListAsync(sp, options, Console.Out, Console.Error, CancellationToken.None);

                case "mcp-token":
                {
                    if (!options.TryGetValue("email", out var tokenEmail) || !tokenEmail.Contains('@'))
                    {
                        Console.Error.WriteLine($"Usage: mcp-token --email <mentor e-mail> [--days <1-{McpTokens.MaxLifetime.TotalDays:0}, default {McpTokens.DefaultLifetime.TotalDays:0}>]");
                        return 2;
                    }
                    var signingKey = app.Configuration[McpComposition.SigningKeySetting];
                    if (string.IsNullOrWhiteSpace(signingKey))
                    {
                        Console.Error.WriteLine($"{McpComposition.SigningKeySetting} is not configured (user-secrets or Mcp__SigningKey; generate the value with the `mcp-key` command).");
                        return 2;
                    }
                    try { McpTokens.SecurityKey(signingKey); }
                    catch (InvalidOperationException ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                        return 2;
                    }
                    var lifetime = McpTokens.DefaultLifetime;
                    if (options.TryGetValue("days", out var d))
                    {
                        if (!int.TryParse(d, out var days) || days < 1 || days > McpTokens.MaxLifetime.TotalDays)
                        {
                            Console.Error.WriteLine($"--days must be a whole number between 1 and {McpTokens.MaxLifetime.TotalDays:0}.");
                            return 2;
                        }
                        lifetime = TimeSpan.FromDays(days);
                    }
                    var users = sp.GetRequiredService<UserManager<AppUser>>();
                    var user = await users.FindByEmailAsync(tokenEmail);
                    if (user is null || !await users.IsInRoleAsync(user, AppRoles.Mentor))
                    {
                        Console.Error.WriteLine("No mentor account with that e-mail.");
                        return 1;
                    }
                    var audience = McpTokens.ResolveAudience(app.Configuration);
                    var token = McpTokens.Create(user.Id, await users.GetSecurityStampAsync(user), audience, lifetime, signingKey, sp.GetRequiredService<TimeProvider>());
                    Console.Error.WriteLine($"MCP bearer token for user {user.Id}, valid {lifetime.TotalDays:0} days, audience {audience}. Keep it secret; it has the mentor's full rights. A password reset of that mentor revokes it.");
                    Console.WriteLine(token);
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Command '{command}' failed: {ex.Message}");
            return 1;
        }
        return 2;
    }

    private static async Task<int> RunMigrationsAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args.Skip(1)
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal) || a.Contains('='))
            .ToArray());
        builder.Logging.ClearProviders();

        IHost host;
        try
        {
            builder.Services.AddMentorPathMigrationServices(builder.Configuration);
            host = builder.Build();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return 2;
        }

        using (host)
        {
            await using var scope = host.Services.CreateAsyncScope();
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var context = await db.CreateDbContextAsync();
                await context.Database.MigrateAsync();
                await WebComposition.EnsureRolesAsync(scope.ServiceProvider);
                Console.WriteLine("Migrations applied.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Command 'migrate' failed ({ex.GetType().Name}). Check database connectivity, permissions, and migration state.");
                return 1;
            }
        }
    }

    internal static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || args[i].Contains('=')) continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            result[key] = value;
        }
        return result;
    }
}
