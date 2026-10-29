using System.ComponentModel;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using ModelContextProtocol.Server;

namespace MentorPath.Web.Mcp.Tools;

/// <summary>Read-only tools about the mentor's mentees. Services are injected by the SDK from the request scope.</summary>
[McpServerToolType]
public sealed class MenteeTools
{
    [McpServerTool(Name = "list_mentees", ReadOnly = true, Idempotent = true)]
    [Description("Lists the mentor's mentees (mentorship relations) in the mentor's own workspace: mentorshipId, display name, e-mail, status (Active, Paused, Closed) and onboardingCompleted. Start here to find a mentorshipId. onboardingCompleted = false means the mentee has not filled in goal, level and challenges yet.")]
    public static async Task<IReadOnlyList<MentorMenteeListItem>> ListMentees(IMentorshipService mentorships, IActorContext actor, CancellationToken ct) =>
        McpResults.Unwrap(await mentorships.ListForMentorAsync(actor.ActorUserId, ct));

    [McpServerTool(Name = "get_mentee_profile", ReadOnly = true, Idempotent = true)]
    [Description("Returns one mentee's onboarding profile: goal, experience level (Junior, Mid, Senior, Lead), biggest challenges, weekly hours available, and the mentor's private notes. Read it before planning or changing a roadmap. If the mentee has not completed onboarding, goal, currentLevel, challenges and weeklyHours are absent from the result: tell the mentor and plan from the catalog and the mentor's brief instead of guessing.")]
    public static async Task<MentorMenteeDetail> GetMenteeProfile(
        IMentorshipService mentorships, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId from list_mentees")] Guid mentorshipId) =>
        McpResults.Unwrap(await mentorships.GetForMentorAsync(actor.ActorUserId, mentorshipId, ct));

    [McpServerTool(Name = "get_mentor_dashboard", ReadOnly = true, Idempotent = true)]
    [Description("The mentor's dashboard, the quickest answer to 'where is each mentee': one row per mentee with hasPublishedRoadmap, completed vs counted topics, pending reviews and last submission, plus submissions waiting for review and recent activity. For the per-topic detail of one mentee use get_roadmap_progress.")]
    public static async Task<MentorDashboardDto> GetMentorDashboard(IProgressService progress, IActorContext actor, CancellationToken ct) =>
        McpResults.Unwrap(await progress.GetMentorDashboardAsync(actor.ActorUserId, ct));
}
