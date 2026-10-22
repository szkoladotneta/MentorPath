using System.Reflection;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Isolation;

public sealed class ArchitectureTests
{
    private static readonly Assembly WebAssembly = typeof(Program).Assembly;

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
