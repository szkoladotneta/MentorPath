using MentorPath.Application.Common;
using ModelContextProtocol;

namespace MentorPath.Web.Mcp;

/// <summary>
/// Maps application results to MCP outcomes. A failed <see cref="Result"/> becomes an <see cref="McpException"/>, which the SDK
/// reports to the client as a tool error with this message, so the agent sees the same error code the UI would.
/// </summary>
internal static class McpResults
{
    public static T Unwrap<T>(Result<T> result) =>
        result.IsSuccess ? result.Value! : throw ToException(result.Error!);

    public static void Unwrap(Result result)
    {
        if (!result.IsSuccess) throw ToException(result.Error!);
    }

    public static Guid ParseId(string value, string name) =>
        Guid.TryParse(value, out var id) ? id : throw new McpException($"{name} must be a GUID.");

    private static McpException ToException(AppError error)
    {
        var fields = error.FieldErrors is { Count: > 0 } f
            ? " " + string.Join("; ", f.Select(x => $"{x.Key}: {string.Join(" ", x.Value)}"))
            : "";
        return new McpException($"{error.Code}: {error.Message}{fields}");
    }
}
