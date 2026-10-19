using System.Text.Json;
using System.Text.Json.Serialization;
using MentorPath.Application.Common;
using MentorPath.Application.Privacy.Contracts;

namespace MentorPath.Web.Cli;

/// <summary>
/// `export-user` and `anonymize-user` (N04, D-42). Prints only user ids and counts: stdout is the production log stream,
/// so the export goes to a file only and no e-mail, answer or file name is ever written to the console.
/// </summary>
internal static class PrivacyCommands
{
    internal static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string ExportUsage = "Usage: export-user (--email <email> | --user-id <guid>) --out <file>";
    private const string AnonymizeUsage = "Usage: anonymize-user (--email <email> | --user-id <guid>) [--confirm]";

    public static async Task<int> ExportAsync(IServiceProvider sp, IReadOnlyDictionary<string, string> options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (!TryParseSubject(options, out var subject)
            || !options.TryGetValue("out", out var path) || string.IsNullOrWhiteSpace(path) || path == "true")
        {
            await stderr.WriteLineAsync(ExportUsage);
            return 2;
        }
        if (File.Exists(path))
        {
            await stderr.WriteLineAsync("Error: the output file already exists; choose a new path.");
            return 1;
        }

        var result = await sp.GetRequiredService<IPersonalDataService>().ExportAsync(subject, ct);
        if (!result.IsSuccess) return await FailAsync(result.Error!, stderr);
        var export = result.Value!;

        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using var file = new FileStream(path, fileOptions);
            await JsonSerializer.SerializeAsync(file, export, ExportJson, ct);
        }
        catch (IOException)
        {
            await stderr.WriteLineAsync("Error: the output file could not be created (it may already exist).");
            return 1;
        }

        await stdout.WriteLineAsync($"Export written. UserId={export.Profile.UserId} Mentorships={export.Mentorships.Count} " +
                                    $"Submissions={export.Submissions.Count} Invitations={export.Invitations.Count}");
        return 0;
    }

    public static async Task<int> AnonymizeAsync(IServiceProvider sp, IReadOnlyDictionary<string, string> options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (!TryParseSubject(options, out var subject))
        {
            await stderr.WriteLineAsync(AnonymizeUsage);
            return 2;
        }
        var apply = options.ContainsKey("confirm");

        var result = await sp.GetRequiredService<IPersonalDataService>().AnonymizeAsync(subject, apply, ct);
        if (!result.IsSuccess) return await FailAsync(result.Error!, stderr);
        var r = result.Value!;

        await stdout.WriteLineAsync(apply ? "Anonymized." : "Dry run, nothing changed. Re-run with --confirm to apply.");
        await stdout.WriteLineAsync($"UserId={r.UserId} AlreadyAnonymized={r.AlreadyAnonymized} MentorshipsClosed={r.MentorshipsClosed} " +
                                    $"MentorshipsScrubbed={r.MentorshipsScrubbed} SubmissionsScrubbed={r.SubmissionsScrubbed} " +
                                    $"AttachmentsDeleted={r.AttachmentsDeleted} FeedbackScrubbed={r.FeedbackScrubbed} " +
                                    $"ReflectionsScrubbed={r.ReflectionsScrubbed} InvitationsScrubbed={r.InvitationsScrubbed}");
        return 0;
    }

    private static bool TryParseSubject(IReadOnlyDictionary<string, string> options, out PersonalDataSubject subject)
    {
        subject = new PersonalDataSubject(null, null);
        var hasEmail = options.TryGetValue("email", out var email) && email != "true" && !string.IsNullOrWhiteSpace(email);
        var hasId = options.TryGetValue("user-id", out var rawId);
        if (hasEmail == hasId) return false;
        if (hasEmail)
        {
            subject = new PersonalDataSubject(null, email);
            return true;
        }
        if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty) return false;
        subject = new PersonalDataSubject(id, null);
        return true;
    }

    // AppError messages never contain the subject's e-mail.
    private static async Task<int> FailAsync(AppError error, TextWriter stderr)
    {
        await stderr.WriteLineAsync($"Error ({error.Code}): {error.Message}");
        return 1;
    }
}
