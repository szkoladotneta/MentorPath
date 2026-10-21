using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Progress;

internal sealed class RoadmapMapService(IDbContextFactory<AppDbContext> dbFactory, IWorkspaceAccessService access) : IRoadmapMapService
{
    private const string Module = "progress";

    private sealed record AssignmentRow(Guid AssignmentId, Guid RoadmapItemId, string ExerciseTitle, bool IsRequired, List<SubmissionStatus> Statuses);

    public async Task<Result<RoadmapMapDto>> GetMapForMenteeAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.Mentee) return Errors.NotFound(Module);
        return await BuildAsync(acc, includeWithdrawn: false, requirePublished: true, ct);
    }

    public async Task<Result<RoadmapMapDto>> GetMapForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null || acc.Role != AccessRole.WorkspaceOwner) return Errors.NotFound(Module);
        return await BuildAsync(acc, includeWithdrawn: true, requirePublished: false, ct);
    }

    private async Task<Result<RoadmapMapDto>> BuildAsync(MentorshipAccess acc, bool includeWithdrawn, bool requirePublished, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.AsNoTracking()
            .Where(r => r.MentorshipId == acc.MentorshipId && r.WorkspaceId == acc.WorkspaceId)
            .Select(r => new { r.Id, r.Title, r.Status }).FirstOrDefaultAsync(ct);
        if (roadmap is null || (requirePublished && roadmap.Status != RoadmapStatus.Published))
            return new RoadmapMapDto(acc.MentorshipId, false, null, 0, 0, 0, []);

        var items = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.WorkspaceId == acc.WorkspaceId && i.RoadmapId == roadmap.Id && (includeWithdrawn || i.IsActive))
            .OrderBy(i => i.Position)
            .Select(i => new { i.Id, i.Position, i.IsActive, i.TopicId, Title = i.Topic!.Title })
            .ToListAsync(ct);
        var itemIds = items.Select(i => i.Id).ToList();
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.WorkspaceId == acc.WorkspaceId && itemIds.Contains(a.RoadmapItemId) && a.IsActive)
            .OrderBy(a => a.Position)
            .Select(a => new AssignmentRow(a.Id, a.RoadmapItemId, a.Exercise!.Title, a.IsRequired,
                a.Submissions.OrderBy(s => s.AttemptNumber).Select(s => s.Status).ToList()))
            .ToListAsync(ct);

        var drafts = items.Select(item =>
        {
            var rows = assignments.Where(a => a.RoadmapItemId == item.Id).Select(a => new RoadmapMapAssignmentDto(
                a.AssignmentId, a.ExerciseTitle, a.IsRequired, ProgressCalculator.ForAssignment(a.Statuses))).ToList();
            var pairs = rows.Select(a => (a.IsRequired, a.Status)).ToList();
            var required = rows.Where(a => a.IsRequired).ToList();
            return (item.Id, item.TopicId, item.Title, item.Position,
                State: RoadmapMapRules.StateOf(item.IsActive, pairs),
                Counts: item.IsActive && ProgressCalculator.CountsTowardsProgress(pairs),
                Done: required.Count(a => a.Status == AssignmentProgressStatus.Completed), Total: required.Count, Rows: rows);
        }).ToList();

        var current = RoadmapMapRules.CurrentNode(drafts.Select(d => (d.Id, d.State, d.Counts)).ToList());
        var counted = drafts.Count(d => d.Counts);
        var completed = drafts.Count(d => d.Counts && d.State == MapNodeState.Completed);

        var catalog = await db.Topics.AsNoTracking().Where(t => t.WorkspaceId == acc.WorkspaceId)
            .Select(t => new MapCatalogTopic(t.Id, t.ParentTopicId, t.Title, t.IsArchived)).ToListAsync(ct);
        var parentOf = catalog.ToDictionary(t => t.Id, t => t.ParentId);
        var activeTopics = items.Where(i => i.IsActive).Select(i => i.TopicId).ToHashSet();
        var planTopics = items.Select(i => i.TopicId).ToHashSet();
        // Withdrawn topics stay plan members even when hidden from the mentee, so they and their subtrees never turn into ghosts.
        var memberTopics = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.WorkspaceId == acc.WorkspaceId && i.RoadmapId == roadmap.Id).Select(i => i.TopicId).ToListAsync(ct);
        var ghosts = RoadmapMapRules.UnplannedDescendants(activeTopics, memberTopics.ToHashSet(), catalog);
        var onMap = planTopics.Concat(ghosts.Select(g => g.Id)).ToHashSet();

        var nodes = drafts.Select(d => new RoadmapMapNodeDto(d.Id, d.Title, d.Position, d.State, d.Id == current, d.Done, d.Total, d.Rows,
                d.TopicId, RoadmapMapRules.NearestAncestorOnMap(d.TopicId, parentOf, onMap)))
            .Concat(ghosts.Select(g => new RoadmapMapNodeDto(Guid.Empty, g.Title, 0, MapNodeState.NotInPlan, false, 0, 0, [],
                g.Id, RoadmapMapRules.NearestAncestorOnMap(g.Id, parentOf, onMap))))
            .ToList();
        return new RoadmapMapDto(acc.MentorshipId, true, roadmap.Title, completed, counted, ProgressCalculator.Percent(completed, counted), nodes);
    }
}
