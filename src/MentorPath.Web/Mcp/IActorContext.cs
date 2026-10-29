using System.Security.Claims;
using ModelContextProtocol;

namespace MentorPath.Web.Mcp;

/// <summary>
/// The user on whose behalf MCP tools run (D-64). Over HTTP it is the bearer token's subject; over stdio it is the mentor
/// named on the command line. Tools pass it as <c>actorUserId</c> to the application services, which do the real authorization.
/// </summary>
public interface IActorContext
{
    Guid ActorUserId { get; }
}

/// <summary>Fixed actor for the stdio transport, set once at startup by the <c>mcp-stdio</c> command.</summary>
public sealed class McpActorOptions
{
    public Guid? FixedActorUserId { get; set; }
}

internal sealed class ActorContext(McpActorOptions options, IHttpContextAccessor? httpContextAccessor = null) : IActorContext
{
    public Guid ActorUserId
    {
        get
        {
            if (options.FixedActorUserId is { } fixedId) return fixedId;
            var user = httpContextAccessor?.HttpContext?.User;
            if (Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id != Guid.Empty) return id;
            throw new McpException("Unauthenticated: this request carries no MentorPath user.");
        }
    }
}
