using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Progress;

internal sealed class ProgressService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IProgressService
{
    private const string Module = "progress";

    private sealed record AssignmentRow(Guid AssignmentId, Guid RoadmapItemId, string ExerciseTitle, bool IsRequired, int Position,
        List<SubmissionRow> Submissions);

    private sealed record SubmissionRow(int AttemptNumber, SubmissionStatus Status, short ConfidenceBefore, short? ConfidenceAfter);

    private sealed record ItemRow(Guid RoadmapItemId, Guid RoadmapId, string TopicTitle, int Position, CompetencyStatus CompetencyStatus);

    public async Task<Result<RoadmapProgressDto>> GetRoadmapProgressAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct)
    {
        var acc = await access.GetMentorshipAccessAsync(actorUserId, mentorshipId, ct);
        if (acc is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var roadmap = await db.Roadmaps.AsNoTracking()
            .Where(r => r.MentorshipId == mentorshipId && r.WorkspaceId == acc.WorkspaceId)
            .Select(r => new { r.Id, r.Status }).FirstOrDefaultAsync(ct);
        var published = roadmap?.Status == RoadmapStatus.Published;
        if (roadmap is null || (acc.Role == AccessRole.Mentee && !published))
            return new RoadmapProgressDto(mentorshipId, false, 0, 0, 0, []);

        var (items, assignments) = await LoadAsync(db, acc.WorkspaceId, [roadmap.Id], ct);
        return Build(mentorshipId, published, items, assignments);
    }

    public async Task<Result<MentorDashboardDto>> GetMentorDashboardAsync(Guid actorUserId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var mentorships = await db.Mentorships.AsNoTracking()
            .Where(m => m.WorkspaceId == ws)
            .OrderBy(m => m.Mentee!.DisplayName)
            .Select(m => new
            {
                m.Id, m.Status, Name = m.Mentee!.DisplayName,
                RoadmapId = m.Roadmap != null ? (Guid?)m.Roadmap.Id : null,
                RoadmapStatus = m.Roadmap != null ? (RoadmapStatus?)m.Roadmap.Status : null,
            })
            .ToListAsync(ct);
        var roadmapIds = mentorships.Where(m => m.RoadmapId is not null).Select(m => m.RoadmapId!.Value).ToList();
        var (items, assignments) = await LoadAsync(db, ws.Value, roadmapIds, ct);

        var pending = await ReviewService.QueryPendingAsync(db, ws.Value, ct);
        var lastSubmissions = await db.Submissions.AsNoTracking()
            .Where(s => s.WorkspaceId == ws)
            .GroupBy(s => s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId)
            .Select(g => new { MentorshipId = g.Key, Last = g.Max(s => s.SubmittedAtUtc) })
            .ToDictionaryAsync(x => x.MentorshipId, x => x.Last, ct);

        var rows = mentorships.Select(m =>
        {
            var progress = m.RoadmapId is null
                ? null
                : Build(m.Id, m.RoadmapStatus == RoadmapStatus.Published,
                    items.Where(i => i.RoadmapId == m.RoadmapId).ToList(), assignments);
            return new MenteeProgressRow(m.Id, m.Name, m.Status, m.RoadmapStatus == RoadmapStatus.Published,
                progress?.CompletedTopics ?? 0, progress?.CountedTopics ?? 0,
                pending.Count(p => p.MentorshipId == m.Id),
                lastSubmissions.TryGetValue(m.Id, out var last) ? last : null);
        }).ToList();

        var since = clock.GetUtcNow().UtcDateTime.AddDays(-30);
        var submitted = await db.Submissions.AsNoTracking()
            .Where(s => s.WorkspaceId == ws && s.SubmittedAtUtc >= since)
            .OrderByDescending(s => s.SubmittedAtUtc).Take(20)
            .Select(s => new ActivityItem(s.SubmittedAtUtc, ActivityKind.Submitted, s.Assignment!.RoadmapItem!.Roadmap!.MentorshipId,
                s.Mentee!.DisplayName, s.Assignment.Exercise!.Title, s.Id))
            .ToListAsync(ct);
        var feedback = await db.MentorFeedback.AsNoTracking()
            .Where(f => f.WorkspaceId == ws && f.CreatedAtUtc >= since)
            .OrderByDescending(f => f.CreatedAtUtc).Take(20)
            .Select(f => new ActivityItem(f.CreatedAtUtc, ActivityKind.FeedbackGiven, f.Submission!.Assignment!.RoadmapItem!.Roadmap!.MentorshipId,
                f.Submission.Mentee!.DisplayName, f.Submission.Assignment.Exercise!.Title, f.SubmissionId))
            .ToListAsync(ct);
        var reflections = await db.SubmissionReflections.AsNoTracking()
            .Where(r => r.Submission!.WorkspaceId == ws && r.CreatedAtUtc >= since)
            .OrderByDescending(r => r.CreatedAtUtc).Take(20)
            .Select(r => new ActivityItem(r.CreatedAtUtc, ActivityKind.ReflectionAdded, r.Submission!.Assignment!.RoadmapItem!.Roadmap!.MentorshipId,
                r.Submission.Mentee!.DisplayName, r.Submission.Assignment.Exercise!.Title, r.SubmissionId))
            .ToListAsync(ct);
        var activity = submitted.Concat(feedback).Concat(reflections).OrderByDescending(a => a.OccurredAtUtc).Take(20).ToList();

        return new MentorDashboardDto(rows, pending, activity);
    }

    private static async Task<(List<ItemRow> Items, List<AssignmentRow> Assignments)> LoadAsync(
        AppDbContext db, Guid workspaceId, List<Guid> roadmapIds, CancellationToken ct)
    {
        if (roadmapIds.Count == 0) return ([], []);
        var items = await db.RoadmapItems.AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && roadmapIds.Contains(i.RoadmapId) && i.IsActive)
            .OrderBy(i => i.Position)
            .Select(i => new ItemRow(i.Id, i.RoadmapId, i.Topic!.Title, i.Position, i.CompetencyStatus))
            .ToListAsync(ct);
        var itemIds = items.Select(i => i.RoadmapItemId).ToList();
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId && itemIds.Contains(a.RoadmapItemId) && a.IsActive)
            .OrderBy(a => a.Position)
            .Select(a => new AssignmentRow(a.Id, a.RoadmapItemId, a.Exercise!.Title, a.IsRequired, a.Position,
                a.Submissions.OrderBy(s => s.AttemptNumber)
                    .Select(s => new SubmissionRow(s.AttemptNumber, s.Status, s.ConfidenceBefore,
                        s.Reflection != null ? (short?)s.Reflection.ConfidenceAfter : null))
                    .ToList()))
            .ToListAsync(ct);
        return (items, assignments);
    }

    private static RoadmapProgressDto Build(Guid mentorshipId, bool published, List<ItemRow> items, List<AssignmentRow> assignments)
    {
        var topics = new List<TopicProgressDto>();
        foreach (var item in items)
        {
            var rows = assignments.Where(a => a.RoadmapItemId == item.RoadmapItemId).ToList();
            var assignmentDtos = rows.Select(a =>
            {
                var latest = a.Submissions.LastOrDefault();
                var latestReflected = a.Submissions.LastOrDefault(s => s.ConfidenceAfter is not null);
                return new AssignmentProgressDto(a.AssignmentId, a.ExerciseTitle, a.IsRequired,
                    ProgressCalculator.ForAssignment(a.Submissions.Select(s => s.Status).ToList()),
                    latest?.ConfidenceBefore, latestReflected?.ConfidenceAfter);
            }).ToList();
            var pairs = assignmentDtos.Select(a => (a.IsRequired, a.Status)).ToList();
            var required = assignmentDtos.Where(a => a.IsRequired).ToList();
            topics.Add(new TopicProgressDto(item.RoadmapItemId, item.TopicTitle, ProgressCalculator.ForTopic(pairs),
                ProgressCalculator.CountsTowardsProgress(pairs),
                required.Count(a => a.Status == AssignmentProgressStatus.Completed), required.Count, assignmentDtos));
        }

        var counted = topics.Count(t => t.CountsTowardsProgress);
        var completed = topics.Count(t => t.CountsTowardsProgress && t.Status == TopicProgressStatus.Completed);
        var demonstrated = items.Count(i => i.CompetencyStatus == CompetencyStatus.Demonstrated);
        var competencyProgress = new CompetencyProgressDto(demonstrated, items.Count,
            ProgressCalculator.Percent(demonstrated, items.Count));
        return new RoadmapProgressDto(mentorshipId, published, completed, counted,
            ProgressCalculator.Percent(completed, counted), topics, competencyProgress);
    }
}
