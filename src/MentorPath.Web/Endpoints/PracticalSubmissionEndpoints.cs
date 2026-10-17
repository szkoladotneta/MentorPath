using MentorPath.Application.Common;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.Web.Composition;
using Microsoft.AspNetCore.Mvc;

namespace MentorPath.Web.Endpoints;

internal static class PracticalSubmissionEndpoints
{
    public static void MapPracticalSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        // Dedicated multipart endpoint (spec §8.2): cookie auth + antiforgery (validated automatically for form-bound endpoints).
        app.MapPost("/api/assignments/{assignmentId:guid}/practical-submissions", async (
                Guid assignmentId,
                [FromForm] Guid clientRequestId,
                [FromForm] string? rationaleMarkdown,
                [FromForm] int confidenceBefore,
                [FromForm] string? externalUrl,
                IFormFile? file,
                HttpContext ctx,
                ISubmissionService submissions,
                CancellationToken ct) =>
            {
                FileUpload? upload = null;
                Stream? stream = null;
                try
                {
                    if (file is not null && file.Length > 0)
                    {
                        stream = file.OpenReadStream();
                        upload = new FileUpload(file.FileName, file.ContentType ?? "application/octet-stream", file.Length, stream);
                    }
                    var request = new SubmitPracticalRequest(assignmentId, clientRequestId, rationaleMarkdown ?? "", confidenceBefore, externalUrl, upload);
                    var result = await submissions.SubmitPracticalAsync(ctx.UserId(), request, ct);
                    return result.IsSuccess ? Results.Ok(result.Value) : EndpointResults.FromError(result.Error!);
                }
                finally
                {
                    if (stream is not null) await stream.DisposeAsync();
                }
            })
            .RequireAuthorization(AppRoles.Mentee)
            .RequireRateLimiting(WebComposition.UploadsRateLimit)
            .WithMetadata(new RequestSizeLimitAttribute(InputLimits.MaxAttachmentBytes + 1024 * 1024))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = InputLimits.MaxAttachmentBytes + 1024 * 1024 });
    }
}
