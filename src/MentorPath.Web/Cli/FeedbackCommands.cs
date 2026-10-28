using System.Globalization;
using MentorPath.Application.Feedback.Contracts;

namespace MentorPath.Web.Cli;

/// <summary>
/// `list-feedback` (N03, D-56): prints product feedback newest first for the operator's prioritization loop.
/// Unlike the privacy commands this prints message text on purpose; it is the command's only output. Run it in a terminal, not a log pipeline.
/// </summary>
internal static class FeedbackCommands
{
    private const string Usage = "Usage: list-feedback [--limit <1-500>]";
    private const int DefaultLimit = 50;

    public static async Task<int> ListAsync(IServiceProvider sp, IReadOnlyDictionary<string, string> options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var limit = DefaultLimit;
        if (options.TryGetValue("limit", out var raw)
            && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 500))
        {
            await stderr.WriteLineAsync(Usage);
            return 2;
        }

        var items = await sp.GetRequiredService<IProductFeedbackOperatorService>().ListAsync(limit, ct);
        foreach (var item in items)
        {
            await stdout.WriteLineAsync($"{item.CreatedAtUtc:yyyy-MM-ddTHH:mm:ssZ} AuthorUserId={item.AuthorUserId} Page={Printable(item.PagePath) ?? "-"}");
            await stdout.WriteLineAsync(item.Message.Length == 0 ? "  (anonymized)" : "  " + Printable(item.Message)!.ReplaceLineEndings("\n  "));
        }
        await stdout.WriteLineAsync($"{items.Count} feedback item(s).");
        return 0;
    }

    // Submitted text is untrusted: drop control characters (ANSI/OSC escapes) so it cannot drive the operator's terminal. Newlines and tabs stay.
    internal static string? Printable(string? value) =>
        value is null ? null : string.Create(value.Length, value, static (span, v) =>
        {
            for (var i = 0; i < v.Length; i++)
                span[i] = char.IsControl(v[i]) && v[i] is not ('\n' or '\r' or '\t') ? '\uFFFD' : v[i];
        });
}
