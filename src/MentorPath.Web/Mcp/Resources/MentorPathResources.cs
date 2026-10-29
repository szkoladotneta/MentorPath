using System.ComponentModel;
using System.Text;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data.Enums;
using ModelContextProtocol.Server;

namespace MentorPath.Web.Mcp.Resources;

/// <summary>Read-only context an agent can attach without calling a tool: a mentee profile and the whole catalog, both as Markdown.</summary>
[McpServerResourceType]
public sealed class MentorPathResources
{
    [McpServerResource(UriTemplate = "mentorpath://mentees/{mentorshipId}/profile", Name = "mentee_profile", MimeType = "text/markdown")]
    [Description("A mentee's onboarding profile (goal, level, challenges, weekly hours) and the mentor's private notes, as Markdown. mentorshipId comes from list_mentees.")]
    public static async Task<string> MenteeProfile(IMentorshipService mentorships, IActorContext actor, CancellationToken ct, string mentorshipId)
    {
        var id = McpResults.ParseId(mentorshipId, nameof(mentorshipId));
        var m = McpResults.Unwrap(await mentorships.GetForMentorAsync(actor.ActorUserId, id, ct));
        var sb = new StringBuilder();
        sb.AppendLine($"# Mentee: {m.MenteeDisplayName}");
        sb.AppendLine();
        sb.AppendLine($"- mentorshipId: `{m.MentorshipId}`");
        sb.AppendLine($"- Status: {m.Status}");
        sb.AppendLine($"- Onboarding: {(m.CurrentLevel is null ? "not completed" : "completed")}");
        sb.AppendLine($"- Experience level: {m.CurrentLevel?.ToString() ?? "unknown"}");
        sb.AppendLine($"- Weekly hours: {m.WeeklyHours?.ToString() ?? "unknown"}");
        sb.AppendLine();
        sb.AppendLine("## Goal");
        sb.AppendLine(string.IsNullOrWhiteSpace(m.Goal) ? "_not provided_" : m.Goal);
        sb.AppendLine();
        sb.AppendLine("## Challenges");
        sb.AppendLine(string.IsNullOrWhiteSpace(m.Challenges) ? "_not provided_" : m.Challenges);
        sb.AppendLine();
        sb.AppendLine("## Mentor's private notes");
        sb.AppendLine(string.IsNullOrWhiteSpace(m.MentorPrivateNotesMarkdown) ? "_none_" : m.MentorPrivateNotesMarkdown);
        return sb.ToString();
    }

    [McpServerResource(UriTemplate = "mentorpath://catalog", Name = "catalog", MimeType = "text/markdown")]
    [Description("The mentor's whole catalog as Markdown: every active topic with its difficulty, learning objectives and exercises grouped by status. Published exercises are the ones that can be assigned to a roadmap.")]
    public static async Task<string> Catalog(ITopicService topics, IActorContext actor, CancellationToken ct)
    {
        var list = McpResults.Unwrap(await topics.ListAsync(actor.ActorUserId, includeArchived: false, ct));
        var sb = new StringBuilder();
        sb.AppendLine("# Catalog");
        sb.AppendLine();
        if (list.Count == 0) sb.AppendLine("_The catalog is empty. Create topics with create_topic._");
        foreach (var t in list.OrderBy(t => t.Category).ThenBy(t => t.Title))
        {
            var detail = McpResults.Unwrap(await topics.GetAsync(actor.ActorUserId, t.TopicId, ct));
            sb.AppendLine($"## {detail.Title} ({detail.Category}, {detail.Difficulty})");
            sb.AppendLine($"- topicId: `{detail.TopicId}`");
            sb.AppendLine($"- Description: {detail.DescriptionMarkdown.ReplaceLineEndings(" ")}");
            sb.AppendLine("- Learning objectives:");
            foreach (var line in detail.LearningObjectivesMarkdown.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sb.AppendLine($"  {line}");
            AppendExercises(sb, "Published exercises (assignable)", detail.Exercises.Where(e => e.Status == ExerciseStatus.Published));
            AppendExercises(sb, "Drafts (not assignable until published)", detail.Exercises.Where(e => e.Status == ExerciseStatus.Draft));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static void AppendExercises(StringBuilder sb, string heading, IEnumerable<ExerciseListItem> exercises)
    {
        var items = exercises.ToList();
        sb.AppendLine($"- {heading}: {(items.Count == 0 ? "none" : "")}");
        foreach (var e in items)
            sb.AppendLine($"  - {e.Title} ({e.Type}, {e.Source}) exerciseId: `{e.ExerciseId}`");
    }
}
