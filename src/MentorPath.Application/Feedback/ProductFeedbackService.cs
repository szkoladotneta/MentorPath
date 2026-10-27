using MentorPath.Application.Common;
using MentorPath.Application.Common.Access;
using MentorPath.Application.Feedback.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Feedback;

internal sealed class ProductFeedbackService(
    IDbContextFactory<AppDbContext> dbFactory,
    IWorkspaceAccessService access,
    TimeProvider clock) : IProductFeedbackService
{
    private const string Module = "feedback";

    public async Task<Result> SubmitAsync(Guid actorUserId, SubmitProductFeedbackRequest request, CancellationToken ct)
    {
        if (actorUserId == Guid.Empty || !await access.HasWorkspaceRelationshipAsync(actorUserId, ct))
            return Errors.NotFound(Module);

        var message = request.Message?.Trim();
        var pagePath = NormalizePagePath(request.PagePath);
        var errors = new FieldErrors();
        errors.Required(nameof(request.Message), message, InputLimits.FeedbackMessage, "Uwaga");
        errors.Optional(nameof(request.PagePath), pagePath, InputLimits.PagePath, "Strona");
        if (errors.HasErrors) return errors.ToError("feedback.invalid");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ProductFeedback.Add(new ProductFeedback
        {
            Id = Guid.NewGuid(),
            AuthorUserId = actorUserId,
            MessageText = message!,
            PageContext = pagePath,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Keeps only the path: a query string or fragment can carry tokens or ids.
    internal static string? NormalizePagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0) path = path[..end];
        if (!path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith("/\\", StringComparison.Ordinal)
            || path.Any(char.IsControl))
            return null;
        return path;
    }
}
