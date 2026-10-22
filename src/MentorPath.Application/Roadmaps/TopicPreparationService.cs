using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Roadmaps;

internal sealed class TopicPreparationService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access) : ITopicPreparationService
{
    private const string Module = "roadmap";

    public async Task<Result<MenteeTopicPreparationDto>> GetTopicForMenteeAsync(
        Guid actorUserId, Guid mentorshipId, Guid roadmapItemId, CancellationToken ct)
    {
        var mentorship = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (mentorship is null
            || mentorship.Role != AccessRole.Mentee
            || mentorship.Status is not (MentorshipStatus.Active or MentorshipStatus.Paused))
            return Errors.NotFound(Module);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.Id == roadmapItemId
                && i.WorkspaceId == mentorship.WorkspaceId
                && i.IsActive
                && i.Roadmap!.WorkspaceId == mentorship.WorkspaceId
                && i.Roadmap.MentorshipId == mentorshipId
                && i.Roadmap.Status == RoadmapStatus.Published
                && i.Roadmap.Mentorship!.MenteeUserId == actorUserId
                && (i.Roadmap.Mentorship.Status == MentorshipStatus.Active
                    || i.Roadmap.Mentorship.Status == MentorshipStatus.Paused))
            .Select(i => new
            {
                i.Id,
                i.TopicId,
                i.MentorNoteMarkdown,
                TopicTitle = i.Topic!.Title,
                TopicDescriptionMarkdown = i.Topic.DescriptionMarkdown,
            })
            .SingleOrDefaultAsync(ct);
        if (item is null) return Errors.NotFound(Module);

        var resources = await db.TopicResources.AsNoTracking()
            .Where(r => r.TopicId == item.TopicId && r.WorkspaceId == mentorship.WorkspaceId)
            .OrderBy(r => r.Position)
            .Select(r => new TopicResourceDto(r.Id, r.Title, r.Url, r.Kind, r.Description, r.Position))
            .ToListAsync(ct);
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.RoadmapItemId == item.Id && a.WorkspaceId == mentorship.WorkspaceId && a.IsActive)
            .OrderBy(a => a.Position)
            .Select(a => new MenteeAssignmentListItem(
                a.Id,
                a.Exercise!.Title,
                a.Exercise.Type,
                a.IsRequired,
                a.DueAtUtc,
                a.Submissions.Where(s => s.MenteeUserId == actorUserId)
                    .OrderByDescending(s => s.AttemptNumber)
                    .Select(s => (SubmissionStatus?)s.Status)
                    .FirstOrDefault(),
                a.Submissions.Count(s => s.MenteeUserId == actorUserId)))
            .ToListAsync(ct);

        return new MenteeTopicPreparationDto(
            mentorshipId,
            item.Id,
            item.TopicTitle,
            item.TopicDescriptionMarkdown,
            item.MentorNoteMarkdown,
            resources,
            assignments);
    }
}
