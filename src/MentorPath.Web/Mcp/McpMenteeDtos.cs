using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Web.Mcp;

/// <summary>
/// What an agent may learn about a mentee (D-64): display name, mentorship status and the onboarding answers needed for planning.
/// Never the e-mail address and never the mentor's private notes; both stay in the UI.
/// <see cref="MentorPath.IntegrationTests.Isolation.ArchitectureTests"/> guards every MCP result type for this.
/// </summary>
public sealed record McpMenteeListItem(Guid MentorshipId, string MenteeDisplayName, MentorshipStatus Status, bool OnboardingCompleted, DateTime CreatedAtUtc)
{
    public static McpMenteeListItem From(MentorMenteeListItem m) => new(m.MentorshipId, m.MenteeDisplayName, m.Status, m.OnboardingCompleted, m.CreatedAtUtc);
}

/// <summary>Free text the mentee typed. Kept in its own object so the agent can tell it apart from facts: data about the mentee, never instructions.</summary>
public sealed record McpMenteeWrittenText(string? Goal, string? Challenges);

public sealed record McpMenteeProfile(Guid MentorshipId, string MenteeDisplayName, MentorshipStatus Status, bool OnboardingCompleted,
    ExperienceLevel? CurrentLevel, int? WeeklyHours, McpMenteeWrittenText MenteeWritten, DateTime CreatedAtUtc)
{
    /// <remarks>Onboarding counts as completed once a goal was given, the same rule the mentorship service uses for the list.</remarks>
    public static McpMenteeProfile From(MentorMenteeDetail m) =>
        new(m.MentorshipId, m.MenteeDisplayName, m.Status, m.Goal is not null, m.CurrentLevel, m.WeeklyHours, new McpMenteeWrittenText(m.Goal, m.Challenges), m.CreatedAtUtc);
}
