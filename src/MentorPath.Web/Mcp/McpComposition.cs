using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MentorPath.Web.Mcp.Prompts;
using MentorPath.Web.Mcp.Resources;
using MentorPath.Web.Mcp.Tools;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;

namespace MentorPath.Web.Mcp;

/// <summary>
/// Hosts the MentorPath MCP server (D-64). HTTP: <c>/mcp</c> inside MentorPath.Web, Streamable HTTP, stateless, bearer JWT.
/// stdio: the <c>mcp-stdio</c> CLI command reuses <see cref="WithMentorPathCapabilities"/> with the stdio transport.
/// </summary>
public static class McpComposition
{
    public const string EndpointPath = "/mcp";
    public const string BearerScheme = "McpBearer";
    public const string Policy = "McpBearer";
    public const string SigningKeySetting = "Mcp:SigningKey";

    /// <summary>
    /// Enums as strings and camelCase, so tool schemas and results read naturally for the model. The SDK marks these options
    /// read-only when it builds the tools, which requires an explicit <see cref="JsonSerializerOptions.TypeInfoResolver"/>.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IServiceCollection AddMentorPathMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<McpActorOptions>();
        services.AddScoped<IActorContext, ActorContext>();
        services.AddMcpServer(ConfigureServer).WithHttpTransport().WithMentorPathCapabilities();

        var key = configuration[SigningKeySetting];
        if (string.IsNullOrWhiteSpace(key)) return services;

        services.AddAuthentication().AddJwtBearer(BearerScheme, o =>
        {
            o.MapInboundClaims = true; // "sub" -> ClaimTypes.NameIdentifier, the same claim the UI and /api use
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = McpTokens.Issuer,
                ValidAudience = McpTokens.Audience,
                IssuerSigningKey = McpTokens.SecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.AddAuthenticationSchemes(BearerScheme).RequireAuthenticatedUser().RequireClaim(ClaimTypes.NameIdentifier));
        return services;
    }

    /// <summary>Maps <c>/mcp</c> when a signing key is configured; without it the endpoint does not exist (nothing to authenticate with).</summary>
    public static void MapMentorPathMcp(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[SigningKeySetting]))
        {
            app.Logger.LogWarning("{Setting} is not configured; the MCP endpoint {Path} is not mapped.", SigningKeySetting, EndpointPath);
            return;
        }
        app.MapMcp(EndpointPath).RequireAuthorization(Policy);
    }

    public static IMcpServerBuilder WithMentorPathCapabilities(this IMcpServerBuilder builder) =>
        builder
            .WithTools<MenteeTools>(SerializerOptions)
            .WithTools<CatalogTools>(SerializerOptions)
            .WithTools<RoadmapTools>(SerializerOptions)
            .WithResources<MentorPathResources>()
            .WithPrompts<MentorPathPrompts>();

    public static void ConfigureServer(McpServerOptions options)
    {
        options.ServerInfo = new() { Name = "mentorpath", Version = "1.0.0" };
        options.ServerInstructions =
            "MentorPath is a mentoring platform for backend (.NET) architecture. You act as the mentor who owns this workspace. " +
            "Tools read mentees, their profiles and progress, the catalog of topics and exercises, and roadmaps; write tools create topics, " +
            "exercise drafts and roadmap drafts. Nothing you create is published: the mentor publishes in the UI. " +
            "About mentees you see their onboarding answers and progress, never their e-mail or the mentor's private notes. " +
            "You may change only Draft roadmaps; a Published roadmap is live for the mentee and only the mentor edits it in the UI. " +
            "All ids are GUIDs returned by the list tools; never invent them. A mentorship has at most one roadmap, so check get_roadmap before create_roadmap. " +
            "Errors come back as '<module>.<code>: <message>'; not_found also covers resources that belong to another mentor. Content is in Polish unless the mentor asks otherwise.";
    }
}

/// <summary>Bearer tokens for agents: HS256 JWT, subject = AppUser.Id, issued by the <c>mcp-token</c> CLI command.</summary>
public static class McpTokens
{
    public const string Issuer = "mentorpath-mcp";
    public const string Audience = "mentorpath-mcp";
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    public static SymmetricSecurityKey SecurityKey(string configuredKey)
    {
        var bytes = Encoding.UTF8.GetBytes(configuredKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException($"{McpComposition.SigningKeySetting} must be at least 32 bytes long.");
        return new SymmetricSecurityKey(bytes);
    }

    public static string Create(Guid userId, TimeSpan lifetime, string configuredKey, TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(SecurityKey(configuredKey), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
