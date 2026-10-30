using System.ComponentModel;
using MentorPath.Application.Common;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using ModelContextProtocol.Server;

namespace MentorPath.Web.Mcp.Tools;

/// <summary>
/// Roadmap tools. A roadmap stays a Draft until the mentor publishes it in the UI; there is deliberately no publish tool, and the write
/// tools refuse Published roadmaps, because those are live for the mentee (D-64).
/// </summary>
[McpServerToolType]
public sealed class RoadmapTools
{
    [McpServerTool(Name = "get_roadmap", ReadOnly = true, Idempotent = true)]
    [Description("Returns, under roadmap, the mentee's roadmap as the mentor sees it: status (Draft or Published), ordered topics (roadmapItemId, topic, mentor note, active flag) and the exercises assigned to each (assignmentId, required flag, due date, submission count). When the mentorship has no roadmap yet, roadmap is null and hint says what to do. A Draft is visible here but not to the mentee; the mentor publishes it in the UI at /mentor/mentees/{mentorshipId}/roadmap.")]
    public static async Task<McpRoadmapView> GetRoadmap(
        IRoadmapService roadmaps, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId from list_mentees")] Guid mentorshipId)
    {
        var roadmap = McpResults.Unwrap(await roadmaps.GetForMentorAsync(actor.ActorUserId, mentorshipId, ct));
        return new McpRoadmapView(mentorshipId, roadmap,
            roadmap is null ? "This mentorship has no roadmap yet. Use create_roadmap, then add_roadmap_topic and assign_exercise." : null);
    }

    [McpServerTool(Name = "get_roadmap_progress", ReadOnly = true, Idempotent = true)]
    [Description("Progress of the mentee on the published roadmap: percent complete, per-topic status (NotStarted, InProgress, Completed) and per-assignment status with the mentee's confidence before and after. Without a published roadmap the result has hasPublishedRoadmap = false and an empty topics list (a Draft does not count); use get_roadmap to see a draft.")]
    public static async Task<RoadmapProgressDto> GetRoadmapProgress(
        IProgressService progress, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId from list_mentees")] Guid mentorshipId) =>
        McpResults.Unwrap(await progress.GetRoadmapProgressAsync(actor.ActorUserId, mentorshipId, ct));

    [McpServerTool(Name = "create_roadmap", Idempotent = false, Destructive = false)]
    [Description("Creates a Draft roadmap for a mentorship. A mentorship has at most one roadmap: call get_roadmap first and extend an existing one instead; a second one fails with roadmap.already_exists. Returns the roadmapId to use with add_roadmap_topic. The mentor publishes it in the UI.")]
    public static async Task<McpRoadmapCreated> CreateRoadmap(
        IRoadmapService roadmaps, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId from list_mentees")] Guid mentorshipId,
        [Description("Roadmap title, max 200 characters")] string title,
        [Description("Objective in Markdown: what the mentee will be able to do at the end")] string objectiveMarkdown)
    {
        var id = McpResults.Unwrap(await roadmaps.CreateAsync(actor.ActorUserId, new CreateRoadmapRequest(mentorshipId, title, objectiveMarkdown), ct));
        return new McpRoadmapCreated(id, RoadmapStatus.Draft,
            $"Add topics with add_roadmap_topic in learning order, then assign published exercises with assign_exercise. The mentor publishes the roadmap at /mentor/mentees/{mentorshipId}/roadmap.");
    }

    [McpServerTool(Name = "add_roadmap_topic", Idempotent = false, Destructive = false)]
    [Description("Adds a catalog topic to the end of a Draft roadmap. Returns the roadmapItemId needed by assign_exercise. Agents can change only Draft roadmaps: a Published roadmap is live for the mentee and the call fails with roadmap.published (propose the change to the mentor instead). A topic can appear once per roadmap (roadmap.topic_exists); archived topics are rejected (roadmap.topic_archived). Use reorder_roadmap_topics to change the order afterwards.")]
    public static async Task<McpRoadmapTopicAdded> AddRoadmapTopic(
        IRoadmapService roadmaps, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId the roadmap belongs to (from list_mentees)")] Guid mentorshipId,
        [Description("The roadmapId from create_roadmap or get_roadmap")] Guid roadmapId,
        [Description("The topicId from list_topics")] Guid topicId,
        [Description("Optional note from the mentor shown to the mentee at this topic, in Markdown")] string? mentorNoteMarkdown = null)
    {
        await RequireDraftAsync(roadmaps, actor, mentorshipId, r => r.RoadmapId == roadmapId, ct);
        var id = McpResults.Unwrap(await roadmaps.AddTopicAsync(actor.ActorUserId, new AddRoadmapTopicRequest(roadmapId, topicId, mentorNoteMarkdown), ct));
        return new McpRoadmapTopicAdded(id, roadmapId, topicId);
    }

    [McpServerTool(Name = "assign_exercise", Idempotent = false, Destructive = false)]
    [Description("Assigns a Published exercise to a topic of a Draft roadmap. Agents can change only Draft roadmaps: a Published roadmap is live for the mentee and the call fails with roadmap.published. The exercise must belong to that topic (roadmap.exercise_topic_mismatch) and must be Published: drafts, including ones created with create_exercise_draft, are rejected with roadmap.exercise_not_published and only the mentor can publish them in the UI. The same exercise cannot be assigned twice to a topic (roadmap.assignment_exists). Returns the assignmentId.")]
    public static async Task<McpExerciseAssigned> AssignExercise(
        IRoadmapService roadmaps, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId the roadmap belongs to (from list_mentees)")] Guid mentorshipId,
        [Description("The roadmapItemId from add_roadmap_topic or get_roadmap")] Guid roadmapItemId,
        [Description("The exerciseId of a Published exercise of the same topic")] Guid exerciseId,
        [Description("Required exercises count towards topic completion; optional ones do not (default true)")] bool isRequired = true,
        [Description("Optional due date and time in UTC, ISO 8601")] DateTime? dueAtUtc = null)
    {
        await RequireDraftAsync(roadmaps, actor, mentorshipId, r => r.Items.Any(i => i.RoadmapItemId == roadmapItemId), ct);
        var id = McpResults.Unwrap(await roadmaps.AssignExerciseAsync(actor.ActorUserId, new AssignExerciseRequest(roadmapItemId, exerciseId, isRequired, dueAtUtc), ct));
        return new McpExerciseAssigned(id, roadmapItemId, exerciseId, isRequired, dueAtUtc);
    }

    [McpServerTool(Name = "reorder_roadmap_topics", Idempotent = true, Destructive = false)]
    [Description("Sets the order of topics on a Draft roadmap (a Published one fails with roadmap.published). Pass every roadmapItemId of the roadmap in the desired order (get_roadmap lists them); a partial list fails with roadmap.invalid_order.")]
    public static async Task<McpRoadmapReordered> ReorderRoadmapTopics(
        IRoadmapService roadmaps, IActorContext actor, CancellationToken ct,
        [Description("The mentorshipId the roadmap belongs to (from list_mentees)")] Guid mentorshipId,
        [Description("The roadmapId from get_roadmap")] Guid roadmapId,
        [Description("All roadmapItemIds of this roadmap, in the new order")] IReadOnlyList<Guid> orderedRoadmapItemIds)
    {
        await RequireDraftAsync(roadmaps, actor, mentorshipId, r => r.RoadmapId == roadmapId, ct);
        McpResults.Unwrap(await roadmaps.ReorderTopicsAsync(actor.ActorUserId, roadmapId, orderedRoadmapItemIds, ct));
        return new McpRoadmapReordered(roadmapId, orderedRoadmapItemIds);
    }

    /// <summary>
    /// Agents change only Draft roadmaps (D-64): a Published roadmap is live for the mentee, so an agent's topic, note or assignment
    /// would reach them without the mentor's review. The ids must belong to this mentorship's roadmap; otherwise the answer is the same
    /// not_found the services give. The check and the write are two calls, so a mentor publishing in between is a benign race.
    /// </summary>
    private static async Task RequireDraftAsync(IRoadmapService roadmaps, IActorContext actor, Guid mentorshipId, Func<MentorRoadmapDto, bool> owns, CancellationToken ct)
    {
        var roadmap = McpResults.Unwrap(await roadmaps.GetForMentorAsync(actor.ActorUserId, mentorshipId, ct));
        if (roadmap is null || !owns(roadmap)) throw McpResults.From(Errors.NotFound("roadmap"));
        if (roadmap.Status != RoadmapStatus.Draft)
            throw McpResults.From(Errors.Validation("roadmap.published",
                $"This roadmap is Published and live for the mentee, so agents cannot change it. Propose the change to the mentor, who edits it at /mentor/mentees/{mentorshipId}/roadmap."));
    }
}
