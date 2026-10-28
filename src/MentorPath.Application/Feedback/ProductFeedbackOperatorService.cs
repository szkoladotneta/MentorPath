using MentorPath.Application.Feedback.Contracts;
using MentorPath.Data;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Application.Feedback;

internal sealed class ProductFeedbackOperatorService(IDbContextFactory<AppDbContext> dbFactory) : IProductFeedbackOperatorService
{
    public async Task<IReadOnlyList<ProductFeedbackListItem>> ListAsync(int limit, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ProductFeedback.AsNoTracking()
            .OrderByDescending(f => f.CreatedAtUtc).ThenByDescending(f => f.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(f => new ProductFeedbackListItem(f.Id, f.AuthorUserId, f.MessageText, f.PageContext, f.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
