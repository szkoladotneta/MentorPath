using System.Security.Claims;
using ModelContextProtocol;

namespace MentorPath.Web.Mcp;

/// <summary>
/// The user on whose behalf MCP tools run (D-64). Over HTTP it is the bearer token's subject; over stdio it is the mentor named on the
/// command line. Tools pass it as <c>actorUserId</c> to the application services, which do the real authorization. Each transport
/// registers its own implementation, so a fixed actor can never exist on the web host.
/// </summary>
public interface IActorContext
{
    Guid ActorUserId { get; }
}

/// <summary>HTTP transport: the bearer token's subject, mapped to <see cref="ClaimTypes.NameIdentifier"/> by the JWT handler.</summary>
internal sealed class HttpActorContext(IHttpContextAccessor httpContextAccessor) : IActorContext
{
    public Guid ActorUserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id != Guid.Empty) return id;
            throw new McpException("Unauthenticated: this request carries no MentorPath user.");
        }
    }
}

/// <summary>stdio transport: one mentor for the whole process, resolved by <c>mcp-stdio --actor</c>. Never registered on the web host.</summary>
internal sealed class FixedActorContext(Guid actorUserId) : IActorContext
{
    public Guid ActorUserId { get; } = actorUserId;
}
