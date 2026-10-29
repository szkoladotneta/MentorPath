using System.Reflection;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data;
using MentorPath.Web.Mcp;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace MentorPath.IntegrationTests.Isolation;

public sealed class ArchitectureTests
{
    private static readonly Assembly WebAssembly = typeof(Program).Assembly;

    /// <summary>The only services an MCP entry point may receive (D-64). Operator-only services, which take no actor, are deliberately absent.</summary>
    private static readonly Type[] McpAllowedServices =
        [typeof(IMentorshipService), typeof(IProgressService), typeof(ITopicService), typeof(IExerciseAuthoringService), typeof(IRoadmapService)];

    [Fact]
    public void Razor_components_never_inject_db_context_or_factory()
    {
        var offenders = WebAssembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(p => p.GetCustomAttribute<InjectAttribute>() is not null)
                .Where(p => p.PropertyType == typeof(AppDbContext) || p.PropertyType == typeof(IDbContextFactory<AppDbContext>))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();
        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Mcp_tools_resources_and_prompts_never_touch_the_db_context()
    {
        var offenders = WebAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("MentorPath.Web.Mcp", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                .SelectMany(m => m.GetParameters())
                .Where(p => p.ParameterType == typeof(AppDbContext) || p.ParameterType == typeof(IDbContextFactory<AppDbContext>))
                .Select(p => $"{t.FullName}.{p.Member.Name}({p.Name})"))
            .ToList();
        offenders.ShouldBeEmpty("MCP code calls Contracts/ interfaces with actorUserId, never the database (D-64)");
    }

    [Fact]
    public void Mcp_entry_points_take_only_contracts_services_the_actor_and_plain_data()
    {
        var methods = WebAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("MentorPath.Web.Mcp", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null
                        || m.GetCustomAttribute<McpServerResourceAttribute>() is not null
                        || m.GetCustomAttribute<McpServerPromptAttribute>() is not null)
            .ToList();
        methods.Count.ShouldBe(19, "15 tools, 2 resources, 2 prompts");
        methods.ShouldAllBe(m => m.IsStatic, "entry points are static, so the SDK never constructs the classes and nothing can arrive through a constructor");
        methods.Select(m => m.DeclaringType!).Distinct().ShouldAllBe(t => t.GetConstructors().All(c => c.GetParameters().Length == 0));
        methods.Where(m => UnwrapTask(m.ReturnType) == typeof(object)).Select(m => m.Name)
            .ShouldBeEmpty("results are named types, so the result walker below can inspect every field an agent receives");

        var offenders = methods
            .SelectMany(m => m.GetParameters().Where(p => !IsAllowed(p.ParameterType))
                .Select(p => $"{m.DeclaringType!.Name}.{m.Name}({p.ParameterType.Name} {p.Name})"))
            .ToList();
        offenders.ShouldBeEmpty("MCP entry points receive services only through Contracts/ interfaces and the actor only through IActorContext; "
                                + "no IServiceProvider, McpServer, RequestContext, HttpContext or DbContext, which could reach the database or another user (D-64)");

        static bool IsAllowed(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(IActorContext) || t == typeof(CancellationToken)) return true;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                var element = t.GetGenericArguments()[0];
                return IsAllowed(element) || (!element.IsInterface && IsContractsType(element));
            }
            if (t.IsInterface) return McpAllowedServices.Contains(t);
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Guid) || t == typeof(DateTime);
        }

        static bool IsContractsType(Type t) =>
            t.Namespace?.StartsWith("MentorPath.Application.", StringComparison.Ordinal) == true && t.Namespace.EndsWith(".Contracts", StringComparison.Ordinal);

        static Type UnwrapTask(Type t) =>
            t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>) ? t.GetGenericArguments()[0] : t;
    }

    [Fact]
    public void Mcp_result_types_never_expose_mentee_email_or_private_notes()
    {
        var offenders = new List<string>();
        var visited = new HashSet<Type>();
        var roots = WebAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("MentorPath.Web.Mcp", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null || m.GetCustomAttribute<McpServerResourceAttribute>() is not null)
            .Select(m => m.ReturnType);
        foreach (var root in roots) Walk(root);
        offenders.ShouldBeEmpty("agents never receive a mentee's e-mail or the mentor's private notes (D-64)");

        void Walk(Type type)
        {
            type = Unwrap(type);
            if (type.Namespace?.StartsWith("MentorPath.", StringComparison.Ordinal) != true || !visited.Add(type)) return;
            foreach (var p in type.GetProperties())
            {
                if (p.Name.Contains("Email", StringComparison.Ordinal) || p.Name.Contains("PrivateNotes", StringComparison.Ordinal))
                    offenders.Add($"{type.Name}.{p.Name}");
                Walk(p.PropertyType);
            }
        }

        static Type Unwrap(Type t)
        {
            while (true)
            {
                if (Nullable.GetUnderlyingType(t) is { } underlying) { t = underlying; continue; }
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>)) { t = t.GetGenericArguments()[0]; continue; }
                if (t != typeof(string) && t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) { t = t.GetGenericArguments()[^1]; continue; }
                return t;
            }
        }
    }

    [Fact]
    public void Mentee_dto_types_do_not_expose_private_mentor_data_or_answers()
    {
        string[] forbidden = ["IsCorrect", "MentorGuide", "MentorPrivateNotes", "ExplanationMarkdown"];
        foreach (var type in new[]
        {
            typeof(MenteeExerciseDto), typeof(MenteeQuizOptionDto), typeof(MenteeTopicPreparationDto),
            typeof(TopicResourceDto), typeof(MenteeAssignmentListItem),
        })
        {
            type.GetProperties().Select(p => p.Name).ShouldNotContain(n => forbidden.Any(f => n.Contains(f)), type.Name);
        }
    }

    [Fact]
    public void Mentor_and_mentee_pages_require_role_authorization()
    {
        var pages = WebAssembly.GetTypes()
            .Select(t => (Type: t, Routes: t.GetCustomAttributes<RouteAttribute>().Select(r => r.Template).ToList()))
            .Where(x => x.Routes.Any(r => r.StartsWith("/mentor", StringComparison.Ordinal) || r.StartsWith("/app", StringComparison.Ordinal)))
            .ToList();
        pages.ShouldNotBeEmpty();
        foreach (var (type, routes) in pages)
        {
            var authorize = type.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
            authorize.ShouldNotBeNull($"{type.Name} ({string.Join(",", routes)}) must declare [Authorize]");
            var expected = routes[0].StartsWith("/mentor", StringComparison.Ordinal) ? "Mentor" : "Mentee";
            authorize.Roles.ShouldBe(expected, type.Name);
        }
    }
}
