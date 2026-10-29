using System.Text.Json;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Web.Mcp;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MentorPath.IntegrationTests.Mcp;

/// <summary>
/// The SDK materialises every tool, resource and prompt from the <c>Mcp/</c> types when the stdio host starts (or on the first HTTP
/// session). Calling the tool methods directly never exercises that path, so this test builds them through the SDK. No database (D-64).
/// </summary>
public sealed class McpServerTests
{
    private static readonly string[] ReadTools =
        ["list_mentees", "get_mentee_profile", "get_mentor_dashboard", "list_topics", "get_topic", "list_exercises", "get_exercise", "get_roadmap", "get_roadmap_progress"];

    private static readonly string[] WriteTools =
        ["create_topic", "create_exercise_draft", "create_roadmap", "add_roadmap_topic", "assign_exercise", "reorder_roadmap_topics"];

    private static ServiceProvider BuildServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The SDK binds a method parameter from DI only when its type is registered; otherwise it becomes a tool input.
        // Registering the Contracts/ interfaces by type (never resolved here) reproduces the real hosts without a database.
        foreach (var contract in new[]
                 {
                     typeof(IActorContext), typeof(IMentorshipService), typeof(IProgressService),
                     typeof(ITopicService), typeof(IExerciseAuthoringService), typeof(IRoadmapService),
                 })
            services.AddScoped(contract, _ => throw new InvalidOperationException($"{contract.Name} is not meant to be resolved in this test."));
        services.AddMcpServer(McpComposition.ConfigureServer).WithMentorPathCapabilities();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sdk_builds_every_tool_resource_and_prompt()
    {
        using var sp = BuildServer();

        sp.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name).ShouldBe([.. ReadTools, .. WriteTools], ignoreOrder: true);
        sp.GetServices<McpServerResource>().Select(r => r.ProtocolResourceTemplate.Name).ShouldBe(["mentee_profile", "catalog"], ignoreOrder: true);
        sp.GetServices<McpServerPrompt>().Select(p => p.ProtocolPrompt.Name).ShouldBe(["plan_roadmap", "draft_exercise"], ignoreOrder: true);
    }

    [Fact]
    public void Read_tools_are_read_only_and_write_tools_are_not()
    {
        using var sp = BuildServer();
        var tools = sp.GetServices<McpServerTool>().ToDictionary(t => t.ProtocolTool.Name, t => t.ProtocolTool);

        foreach (var name in ReadTools) tools[name].Annotations?.ReadOnlyHint.ShouldBe(true, name);
        foreach (var name in WriteTools) (tools[name].Annotations?.ReadOnlyHint ?? false).ShouldBeFalse(name);
    }

    [Fact]
    public void Every_tool_and_parameter_is_described_and_enums_are_listed_as_strings()
    {
        using var sp = BuildServer();

        foreach (var tool in sp.GetServices<McpServerTool>().Select(t => t.ProtocolTool))
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
            if (!tool.InputSchema.TryGetProperty("properties", out var properties)) continue;
            foreach (var property in properties.EnumerateObject())
                property.Value.TryGetProperty("description", out _).ShouldBeTrue($"{tool.Name}.{property.Name} has no description");
        }

        var difficulty = sp.GetServices<McpServerTool>().Single(t => t.ProtocolTool.Name == "create_topic")
            .ProtocolTool.InputSchema.GetProperty("properties").GetProperty("difficulty");
        difficulty.ToString().ShouldContain("Intermediate", customMessage: "enums must be offered to the model as their names, not numbers");
    }
}
