using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Data;
using MentorPath.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Catalog;

internal sealed class RubricService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IRubricService
{
    private const string Module = "rubric";
    private const int LevelCount = 5;

    public async Task<Result<IReadOnlyList<RubricCriterionDto>>> ListCriteriaAsync(Guid actorUserId, bool includeInactive, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSeededAsync(db, ws.Value, clock, ct);
        var rows = await db.RubricCriteria.AsNoTracking()
            .Where(c => c.WorkspaceId == ws && (includeInactive || c.IsActive))
            .OrderBy(c => c.Position).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result> UpdateCriterionAsync(Guid actorUserId, Guid criterionId, UpdateRubricCriterionRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var criterion = await db.RubricCriteria.FirstOrDefaultAsync(c => c.Id == criterionId && c.WorkspaceId == ws, ct);
        if (criterion is null) return Errors.NotFound(Module);

        var errors = new FieldErrors();
        errors.Required(nameof(request.Name), request.Name, 100, "Nazwa kryterium");
        var levels = request.LevelDescriptions ?? [];
        if (levels.Count != LevelCount) errors.Add(nameof(request.LevelDescriptions), "Podaj opisy dla wszystkich 5 poziomów.");
        else
            for (var i = 0; i < LevelCount; i++)
                errors.Required($"{nameof(request.LevelDescriptions)}[{i}]", levels[i], InputLimits.ShortText, $"Opis poziomu {i + 1}");
        if (errors.HasErrors) return errors.ToError("rubric.invalid");

        criterion.Name = request.Name.Trim();
        criterion.Level1Description = levels[0].Trim();
        criterion.Level2Description = levels[1].Trim();
        criterion.Level3Description = levels[2].Trim();
        criterion.Level4Description = levels[3].Trim();
        criterion.Level5Description = levels[4].Trim();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Errors.Conflict("rubric.name_taken", "Kryterium o takiej nazwie już istnieje.");
        }
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(Guid actorUserId, Guid criterionId, bool isActive, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.RubricCriteria.AsNoTracking()
            .Where(c => c.Id == criterionId && c.WorkspaceId == ws).Select(c => (bool?)c.IsActive).FirstOrDefaultAsync(ct);
        if (current is null) return Errors.NotFound(Module);
        if (current == isActive) return Result.Success();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Serializes concurrent changes within the workspace so the "last active criterion" guard cannot be raced.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({ws.Value.ToString()}::text, 0::bigint))", ct);
        if (!isActive && !await db.RubricCriteria.AnyAsync(c => c.WorkspaceId == ws && c.IsActive && c.Id != criterionId, ct))
            return Errors.Conflict("rubric.last_active_criterion", "Co najmniej jedno kryterium musi pozostać aktywne.");

        await db.RubricCriteria.Where(c => c.Id == criterionId && c.WorkspaceId == ws)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.IsActive, isActive), ct);
        await tx.CommitAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(Guid actorUserId, IReadOnlyList<Guid> criterionIds, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await EnsureSeededAsync(db, ws.Value, clock, ct);
        var ordered = criterionIds ?? [];
        var criteria = await db.RubricCriteria.Where(c => c.WorkspaceId == ws).ToListAsync(ct);
        var byId = criteria.ToDictionary(c => c.Id);
        if (ordered.Any(id => !byId.ContainsKey(id))) return Errors.NotFound(Module);
        if (ordered.Distinct().Count() != ordered.Count || ordered.Count != criteria.Count)
            return Errors.Validation("rubric.invalid_order", "Podaj wszystkie kryteria dokładnie raz.");

        for (var i = 0; i < ordered.Count; i++) byId[ordered[i]].Position = i;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Seeds the default criteria for a workspace that has none (workspaces created after the N10 migration).</summary>
    internal static async Task EnsureSeededAsync(AppDbContext db, Guid workspaceId, TimeProvider clock, CancellationToken ct)
    {
        if (await db.RubricCriteria.AnyAsync(c => c.WorkspaceId == workspaceId, ct)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var added = new List<RubricCriterion>();
        for (var i = 0; i < DefaultRubric.Criteria.Count; i++)
        {
            var d = DefaultRubric.Criteria[i];
            var criterion = new RubricCriterion
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspaceId,
                Name = d.Name,
                Position = i,
                IsActive = true,
                Level1Description = d.Levels[0],
                Level2Description = d.Levels[1],
                Level3Description = d.Levels[2],
                Level4Description = d.Levels[3],
                Level5Description = d.Levels[4],
                CreatedAtUtc = now,
            };
            added.Add(criterion);
            db.RubricCriteria.Add(criterion);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // A concurrent request seeded the same workspace first; detach only our rows so the caller's tracked entities survive.
            foreach (var criterion in added) db.Entry(criterion).State = EntityState.Detached;
        }
    }

    internal static RubricCriterionDto ToDto(RubricCriterion c) => new(c.Id, c.Name, c.Position, c.IsActive,
        [c.Level1Description, c.Level2Description, c.Level3Description, c.Level4Description, c.Level5Description]);
}
