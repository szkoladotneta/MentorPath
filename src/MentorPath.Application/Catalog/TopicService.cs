using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Catalog;

internal sealed class TopicService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : ITopicService
{
    private const string Module = "catalog";

    public async Task<Result<IReadOnlyList<TopicListItem>>> ListAsync(Guid actorUserId, bool includeArchived, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.Topics.AsNoTracking()
            .Where(t => t.WorkspaceId == ws && (includeArchived || !t.IsArchived))
            .OrderBy(t => t.Category).ThenBy(t => t.Title)
            .Select(t => new TopicListItem(t.Id, t.Title, t.Slug, t.Category, t.Difficulty, t.IsArchived,
                db.Exercises.Count(e => e.WorkspaceId == ws && e.TopicId == t.Id)))
            .ToListAsync(ct);
        return items;
    }

    public async Task<Result<TopicDetail>> GetAsync(Guid actorUserId, Guid topicId, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId && t.WorkspaceId == ws, ct);
        if (topic is null) return Errors.NotFound(Module);
        var exercises = await db.Exercises.AsNoTracking()
            .Where(e => e.WorkspaceId == ws && e.TopicId == topicId)
            .OrderBy(e => e.Status).ThenByDescending(e => e.CreatedAtUtc)
            .Select(e => new ExerciseListItem(e.Id, e.TopicId, e.Title, e.Type, e.Status, e.Source, e.CreatedAtUtc))
            .ToListAsync(ct);
        return new TopicDetail(topic.Id, topic.Title, topic.Slug, topic.Category, topic.Difficulty, topic.DescriptionMarkdown,
            topic.LearningObjectivesMarkdown, topic.IsArchived, exercises, WhyItMattersMarkdown: topic.WhyItMattersMarkdown);
    }

    public async Task<Result<Guid>> CreateAsync(Guid actorUserId, UpsertTopicRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = Validate(request);
        if (errors.HasErrors) return errors.ToError("catalog.invalid_topic");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = new Topic
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = ws.Value,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        Apply(topic, request);
        db.Topics.Add(topic);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return SlugTaken();
        }
        return topic.Id;
    }

    public async Task<Result> UpdateAsync(Guid actorUserId, Guid topicId, UpsertTopicRequest request, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        var errors = Validate(request);
        if (errors.HasErrors) return errors.ToError("catalog.invalid_topic");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var topic = await db.Topics.FirstOrDefaultAsync(t => t.Id == topicId && t.WorkspaceId == ws, ct);
        if (topic is null) return Errors.NotFound(Module);
        Apply(topic, request);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return SlugTaken();
        }
        return Result.Success();
    }

    public async Task<Result> SetArchivedAsync(Guid actorUserId, Guid topicId, bool isArchived, CancellationToken ct)
    {
        var ws = await access.GetOwnedWorkspaceIdAsync(actorUserId, ct);
        if (ws is null) return Errors.NotFound(Module);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Topics.Where(t => t.Id == topicId && t.WorkspaceId == ws)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsArchived, isArchived), ct);
        return rows == 1 ? Result.Success() : Errors.NotFound(Module);
    }

    private static FieldErrors Validate(UpsertTopicRequest r)
    {
        var e = new FieldErrors();
        e.Required(nameof(r.Title), r.Title, InputLimits.Title, "Tytuł");
        e.Required(nameof(r.Slug), r.Slug, InputLimits.Slug, "Slug");
        if (!string.IsNullOrWhiteSpace(r.Slug) && !ExerciseValidator.IsValidSlug(r.Slug.Trim()))
            e.Add(nameof(r.Slug), "Slug może zawierać tylko małe litery, cyfry i myślniki.");
        e.Required(nameof(r.Category), r.Category, InputLimits.Category, "Kategoria");
        if (!Enum.IsDefined(r.Difficulty)) e.Add(nameof(r.Difficulty), "Wybierz poziom trudności.");
        e.Required(nameof(r.DescriptionMarkdown), r.DescriptionMarkdown, InputLimits.Markdown, "Opis");
        e.Required(nameof(r.LearningObjectivesMarkdown), r.LearningObjectivesMarkdown, InputLimits.Markdown, "Cele edukacyjne");
        e.Optional(nameof(r.WhyItMattersMarkdown), r.WhyItMattersMarkdown, InputLimits.Markdown, "Dlaczego to ważne");
        return e;
    }

    private static void Apply(Topic t, UpsertTopicRequest r)
    {
        t.Title = r.Title.Trim();
        t.Slug = r.Slug.Trim();
        t.Category = r.Category.Trim();
        t.Difficulty = r.Difficulty;
        t.DescriptionMarkdown = r.DescriptionMarkdown.Trim();
        t.LearningObjectivesMarkdown = r.LearningObjectivesMarkdown.Trim();
        t.WhyItMattersMarkdown = Text.NullIfBlank(r.WhyItMattersMarkdown);
    }

    private static AppError SlugTaken() => Errors.Validation("catalog.slug_taken", "Formularz zawiera błędy.",
        new Dictionary<string, string[]> { [nameof(UpsertTopicRequest.Slug)] = ["Temat o tym slugu już istnieje w Twoim katalogu."] });
}
