using MentorPath.Application.Common;

namespace MentorPath.Web.Endpoints;

internal static class EndpointResults
{
    public static IResult FromError(AppError error) => error.Kind switch
    {
        ErrorKind.NotFound => Results.NotFound(),
        ErrorKind.Validation => Results.BadRequest(error),
        ErrorKind.Conflict => Results.Conflict(error),
        ErrorKind.QuotaExceeded => Results.Json(error, statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Json(error, statusCode: StatusCodes.Status503ServiceUnavailable),
    };

    public static Guid UserId(this HttpContext ctx) =>
        Guid.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
}
