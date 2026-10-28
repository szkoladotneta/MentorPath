namespace MentorPath.Web.BackgroundJobs;

internal sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    public int IntervalSeconds { get; set; } = 60;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
