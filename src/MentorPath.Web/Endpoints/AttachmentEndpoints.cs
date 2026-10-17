using MentorPath.Application.Submissions.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace MentorPath.Web.Endpoints;

internal static class AttachmentEndpoints
{
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attachments/{attachmentId:guid}", async (Guid attachmentId, HttpContext ctx, ISubmissionService submissions, CancellationToken ct) =>
        {
            var result = await submissions.OpenAttachmentAsync(ctx.UserId(), attachmentId, ct);
            if (!result.IsSuccess) return EndpointResults.FromError(result.Error!);
            ctx.Response.Headers.CacheControl = "private, no-store";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            var file = result.Value!;
            return Results.File(file.Content, file.ContentType, file.FileName);
        }).RequireAuthorization();

        app.MapGet("/api/antiforgery-token", (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { headerName = tokens.HeaderName, requestToken = tokens.RequestToken });
        }).RequireAuthorization();
    }
}
