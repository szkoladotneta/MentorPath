using System.Globalization;

namespace MentorPath.Web.Components.Pages.Mentor.Insights;

internal static class MentorInsightsDurationFormatter
{
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");

    public static string FormatHours(TimeSpan? duration) =>
        duration is null ? "—" : $"{duration.Value.TotalHours.ToString("0.0", PolishCulture)} godz.";

    public static string FormatMinutes(TimeSpan? duration) =>
        duration is null ? "—" : $"{duration.Value.TotalMinutes.ToString("0.#", PolishCulture)} min";
}
