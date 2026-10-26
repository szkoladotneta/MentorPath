using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Progress;

internal sealed class MentorInsightsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IMentorInsightsService
{
    private const string Module = "progress";

    public async Task<Result<MentorInsightsDto>> GetMentorInsightsAsync(Guid actorUserId, CancellationToken ct)
    {
        var workspaceId = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (workspaceId is null) return Errors.NotFound(Module);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var windowStart = now.AddDays(-30);

        var matureAssignmentOpens = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId
                        && e.EventType == ActivityEventType.AssignmentOpened
                        && e.OccurredAtUtc >= windowStart
                        && e.OccurredAtUtc <= now.AddDays(-14))
            .Select(e => new { e.AssignmentId, e.OccurredAtUtc })
            .ToListAsync(ct);
        var openedAssignmentIds = matureAssignmentOpens.Select(e => e.AssignmentId).Distinct().ToList();
        var assignmentSubmissions = openedAssignmentIds.Count == 0
            ? []
            : await db.Submissions.AsNoTracking()
                .Where(s => s.WorkspaceId == workspaceId
                            && openedAssignmentIds.Contains(s.AssignmentId)
                            && s.SubmittedAtUtc <= now)
                .Select(s => new { s.AssignmentId, s.SubmittedAtUtc })
                .ToListAsync(ct);
        var submissionsByAssignment = assignmentSubmissions.ToLookup(s => s.AssignmentId);
        var assignmentConversions = matureAssignmentOpens.Count(e =>
            submissionsByAssignment[e.AssignmentId].Any(s =>
                s.SubmittedAtUtc >= e.OccurredAtUtc && s.SubmittedAtUtc <= e.OccurredAtUtc.AddDays(14)));

        var reviewedSubmissions = await db.Submissions.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId
                        && s.SubmittedAtUtc >= windowStart
                        && s.SubmittedAtUtc <= now
                        && s.Feedback != null
                        && s.Feedback.WorkspaceId == workspaceId
                        && s.Feedback.CreatedAtUtc <= now)
            .Select(s => new { s.SubmittedAtUtc, ReviewedAtUtc = s.Feedback!.CreatedAtUtc })
            .ToListAsync(ct);
        var submittedToReviewed = reviewedSubmissions
            .Select(s => s.ReviewedAtUtc - s.SubmittedAtUtc)
            .Where(duration => duration >= TimeSpan.Zero)
            .ToList();

        var matureNeedsRevisionFeedback = await db.MentorFeedback.AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId
                        && f.Verdict == ReviewVerdict.NeedsRevision
                        && f.CreatedAtUtc >= windowStart
                        && f.CreatedAtUtc <= now.AddDays(-7))
            .Select(f => new
            {
                f.Submission!.AssignmentId,
                f.Submission.MenteeUserId,
                f.Submission.AttemptNumber,
                f.CreatedAtUtc,
            })
            .ToListAsync(ct);
        var revisionAssignmentIds = matureNeedsRevisionFeedback.Select(f => f.AssignmentId).Distinct().ToList();
        var laterAttempts = revisionAssignmentIds.Count == 0
            ? []
            : await db.Submissions.AsNoTracking()
                .Where(s => s.WorkspaceId == workspaceId
                            && revisionAssignmentIds.Contains(s.AssignmentId)
                            && s.SubmittedAtUtc <= now)
                .Select(s => new { s.AssignmentId, s.MenteeUserId, s.AttemptNumber, s.SubmittedAtUtc })
                .ToListAsync(ct);
        var attemptsByAssignment = laterAttempts.ToLookup(s => s.AssignmentId);
        var revisionConversions = matureNeedsRevisionFeedback.Count(f =>
            attemptsByAssignment[f.AssignmentId].Any(s =>
                s.MenteeUserId == f.MenteeUserId
                && s.AttemptNumber > f.AttemptNumber
                && s.SubmittedAtUtc >= f.CreatedAtUtc
                && s.SubmittedAtUtc <= f.CreatedAtUtc.AddDays(7)));

        var reviewDurations = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId
                        && e.EventType == ActivityEventType.ReviewSubmitted
                        && e.OccurredAtUtc >= windowStart
                        && e.OccurredAtUtc <= now
                        && e.ReviewDuration != null)
            .Select(e => e.ReviewDuration!.Value)
            .ToListAsync(ct);

        return new MentorInsightsDto(
            windowStart,
            now,
            new ConversionMetricDto(assignmentConversions, matureAssignmentOpens.Count),
            Median(submittedToReviewed),
            new ConversionMetricDto(revisionConversions, matureNeedsRevisionFeedback.Count),
            Median(reviewDurations));
    }

    private static TimeSpan? Median(List<TimeSpan> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        var middle = values.Count / 2;
        if (values.Count % 2 == 1) return values[middle];
        return TimeSpan.FromTicks(values[middle - 1].Ticks + (values[middle].Ticks - values[middle - 1].Ticks) / 2);
    }
}
