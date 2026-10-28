using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Common.Access;

internal sealed class WorkspaceAccessService(IDbContextFactory<AppDbContext> dbFactory) : IWorkspaceAccessService
{
    public async Task<Guid?> GetOwnedWorkspaceIdAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Workspaces.AsNoTracking()
            .Where(w => w.OwnerUserId == userId)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> HasWorkspaceRelationshipAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Workspaces.AsNoTracking().AnyAsync(w => w.OwnerUserId == userId, ct)
               || await db.Mentorships.AsNoTracking().AnyAsync(m => m.MenteeUserId == userId, ct);
    }

    public async Task<bool> CanManageWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Workspaces.AsNoTracking().AnyAsync(w => w.Id == workspaceId && w.OwnerUserId == userId, ct);
    }

    public async Task<MentorshipAccess?> GetMentorshipAccessAsync(Guid userId, Guid mentorshipId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Mentorships.AsNoTracking()
            .Where(m => m.Id == mentorshipId)
            .Select(m => new { m.Id, m.WorkspaceId, m.MenteeUserId, m.Status, OwnerUserId = m.Workspace!.OwnerUserId })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        if (row.OwnerUserId == userId)
            return new MentorshipAccess(row.Id, row.WorkspaceId, row.MenteeUserId, row.Status, AccessRole.WorkspaceOwner);
        if (row.MenteeUserId == userId && row.Status != MentorshipStatus.Closed)
            return new MentorshipAccess(row.Id, row.WorkspaceId, row.MenteeUserId, row.Status, AccessRole.Mentee);
        return null;
    }

    public async Task<bool> CanAccessMentorshipAsync(Guid userId, Guid mentorshipId, CancellationToken ct) =>
        await GetMentorshipAccessAsync(userId, mentorshipId, ct) is not null;

    public async Task<AssignmentAccess?> GetAssignmentAccessAsync(Guid userId, Guid assignmentId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Assignments.AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .Select(a => new
            {
                a.Id,
                a.WorkspaceId,
                a.ExerciseId,
                a.IsActive,
                ItemActive = a.RoadmapItem!.IsActive,
                RoadmapStatus = a.RoadmapItem.Roadmap!.Status,
                MentorshipId = a.RoadmapItem.Roadmap.MentorshipId,
                MenteeUserId = a.RoadmapItem.Roadmap.Mentorship!.MenteeUserId,
                MentorshipStatus = a.RoadmapItem.Roadmap.Mentorship.Status,
                OwnerUserId = a.RoadmapItem.Roadmap.Mentorship.Workspace!.OwnerUserId,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return null;

        AccessRole role;
        if (row.OwnerUserId == userId) role = AccessRole.WorkspaceOwner;
        else if (row.MenteeUserId == userId
                 && row.MentorshipStatus != MentorshipStatus.Closed
                 && row.RoadmapStatus == RoadmapStatus.Published) role = AccessRole.Mentee;
        else return null;

        return new AssignmentAccess(row.Id, row.WorkspaceId, row.MentorshipId, row.MenteeUserId, row.ExerciseId, role,
            row.MentorshipStatus, row.RoadmapStatus, row.IsActive, row.ItemActive);
    }

    public async Task<bool> CanAccessSubmissionAsync(Guid userId, Guid submissionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Submissions.AsNoTracking()
            .Where(s => s.Id == submissionId)
            .Select(s => new
            {
                s.MenteeUserId,
                MentorshipStatus = s.Assignment!.RoadmapItem!.Roadmap!.Mentorship!.Status,
                OwnerUserId = s.Assignment.RoadmapItem.Roadmap.Mentorship.Workspace!.OwnerUserId,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null) return false;
        return row.OwnerUserId == userId
               || (row.MenteeUserId == userId && row.MentorshipStatus != MentorshipStatus.Closed);
    }
}
