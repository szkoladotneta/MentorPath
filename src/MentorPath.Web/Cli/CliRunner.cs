using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Web.Composition;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Web.Cli;

/// <summary>Administrative commands executed from the same binary (D-24): migrate, create-mentor, seed-demo, export-user, anonymize-user, list-feedback.</summary>
public static class CliRunner
{
    private static readonly string[] Commands = ["migrate", "create-mentor", "seed-demo", "export-user", "anonymize-user", "list-feedback"];

    public static bool IsCliCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0], StringComparer.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());
        var builder = WebApplication.CreateBuilder(args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal) || a.Contains('=')).ToArray());
        builder.Configuration["Storage:EnsureContainerOnStartup"] = "false";
        builder.AddMentorPathWeb();
        await using var app = builder.Build();
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
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Command '{command}' failed: {ex.Message}");
            return 1;
        }
        return 2;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
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
