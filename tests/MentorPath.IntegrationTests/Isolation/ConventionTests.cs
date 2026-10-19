using System.Reflection;
using System.Text.RegularExpressions;
using MentorPath.Application.Common;

namespace MentorPath.IntegrationTests.Isolation;

/// <summary>
/// Mechanical enforcement of repository invariants from .github/copilot-instructions.md.
/// Source-scan tests are deliberately heuristic: they exist to fail loudly when a rule is bypassed.
/// </summary>
public sealed class ConventionTests
{
    private static readonly Assembly ApplicationAssembly = typeof(Result).Assembly;
    private static readonly string RepoRoot = FindRepoRoot();

    // Methods that are intentionally not actor-scoped: anonymous invitation flow and the operator CLI.
    private static readonly HashSet<string> ActorlessMethods =
    [
        "IInvitationService.PreviewAsync",
        "IInvitationService.AcceptWithNewAccountAsync",
        "IMentorProvisioningService.ProvisionMentorAsync",
        "IPersonalDataService.ExportAsync",
        "IPersonalDataService.AnonymizeAsync",
    ];

    // Actorless methods with no resource to probe, so no NotFound contract. Every other actorless method
    // (e.g. IPersonalDataService: unknown subject -> privacy.not_found) stays under the NotFound coverage test.
    private static readonly HashSet<string> NoNotFoundContract =
    [
        "IInvitationService.PreviewAsync",
        "IInvitationService.AcceptWithNewAccountAsync",
        "IMentorProvisioningService.ProvisionMentorAsync",
    ];

    // Actorless services bypass IWorkspaceAccessService, so only operator CLI commands may call them.
    private static readonly string[] OperatorServices = ["IMentorProvisioningService", "IPersonalDataService"];

    private static readonly string[] OperatorServiceAllowedDirs =
    [
        "src/MentorPath.Web/Cli/",
        "src/MentorPath.Application/Workspaces/",
        "src/MentorPath.Application/Privacy/",
    ];

    // Which module may insert/update/delete which tables (docs/DOMAIN_RULES.md, write ownership).
    private static readonly Dictionary<string, string[]> WriteOwnership = new()
    {
        ["Workspaces"] = ["Workspaces"],
        ["Mentorships"] = ["Mentorships"],
        ["Invitations"] = ["Invitations", "Mentorships"],
        ["Catalog"] = ["Topics", "Exercises", "QuizOptions"],
        ["AiAuthoring"] = ["AiGenerationRequests"],
        ["Roadmaps"] = ["Roadmaps", "RoadmapItems", "Assignments"],
        ["Submissions"] = ["Submissions", "SubmissionAttachments"],
        ["Reviews"] = ["MentorFeedback", "SubmissionReflections", "Submissions"],
        ["Progress"] = [],
        ["Privacy"] = ["Mentorships", "Submissions", "SubmissionAttachments", "MentorFeedback", "SubmissionReflections", "Invitations"],
    };

    // Service methods not yet covered by a NotFound isolation test. Shrink this list, never grow it.
    private static readonly HashSet<string> IsolationCoverageDebt = [];

    // Actor-scoped lists take no resource id: they return only the actor's own rows, so there is nothing foreign to probe by id.
    private static readonly HashSet<string> ActorScopedLists =
    [
        "IReviewService.GetPendingAsync", "IMentorshipService.ListForMenteeAsync", "IMentorshipService.ListForMentorAsync",
        "IInvitationService.ListAsync", "ITopicService.ListAsync", "IExerciseAuthoringService.ListAsync", "IAiExerciseDraftService.GetQuotaAsync",
    ];

    private static IEnumerable<MethodInfo> ServiceMethods() =>
        ApplicationAssembly.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic
                        && t.Namespace is { } ns && ns.EndsWith(".Contracts", StringComparison.Ordinal)
                        && Regex.IsMatch(t.Name, "^I\\w+Service$"))
            .SelectMany(t => t.GetMethods());

    [Fact]
    public void Service_methods_take_actorUserId_first_unless_explicitly_exempt()
    {
        var methods = ServiceMethods().ToList();
        methods.ShouldNotBeEmpty();

        var offenders = methods
            .Where(m => !ActorlessMethods.Contains($"{m.DeclaringType!.Name}.{m.Name}"))
            .Where(m => m.GetParameters() is not [{ Name: "actorUserId" } first, ..] || first.ParameterType != typeof(Guid))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        offenders.ShouldBeEmpty("public service methods must start with `Guid actorUserId`");
    }

    [Fact]
    public void Service_methods_are_covered_by_NotFound_isolation_tests()
    {
        var testSources = SourceFiles("tests/MentorPath.IntegrationTests")
            .Where(f => !f.Replace('\\', '/').Contains("/Infrastructure/", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToList();

        // A call counts as covered when the same statement asserts NotFound (ShouldBeNotFound / ErrorKind.NotFound).
        // A contract with no implementation yet (schema-change PR before the feature PR) cannot be called; it is checked once implemented.
        var uncovered = ServiceMethods()
            .Where(m => !NoNotFoundContract.Contains($"{m.DeclaringType!.Name}.{m.Name}"))
            .Where(m => HasImplementation(m.DeclaringType!))
            .Select(m => (Key: $"{m.DeclaringType!.Name}.{m.Name}", Pattern: new Regex($@"\.{m.Name}\([^;]*NotFound", RegexOptions.Singleline)))
            .Where(x => !testSources.Any(s => x.Pattern.IsMatch(s)))
            .Select(x => x.Key)
            .Except(IsolationCoverageDebt)
            .Except(ActorScopedLists)
            .ToList();

        uncovered.ShouldBeEmpty("every service method needs an integration test asserting NotFound for a foreign actor");
    }

    [Fact]
    public void Data_layer_has_no_global_query_filters()
    {
        var offenders = SourceFiles("src/MentorPath.Data")
            .Where(f => !f.Contains("Migrations", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("HasQueryFilter", StringComparison.Ordinal))
            .ToList();
        offenders.ShouldBeEmpty("global query filters are forbidden (D-22)");
    }

    [Fact]
    public void Production_code_uses_TimeProvider_instead_of_wall_clock()
    {
        var clock = new Regex(@"\b(DateTime|DateTimeOffset)\.(UtcNow|Now|Today)\b");
        var offenders = SourceFiles("src")
            .Where(f => !f.Contains("Migrations", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("DemoSeed.cs", StringComparison.Ordinal)) // dev-only seeding tool
            .Where(f => clock.IsMatch(File.ReadAllText(f)))
            .ToList();
        offenders.ShouldBeEmpty("use TimeProvider.GetUtcNow()");
    }

    [Fact]
    public void MarkupString_is_used_only_by_the_Markdown_component()
    {
        var offenders = SourceFiles("src")
            .Where(f => !f.EndsWith("Markdown.razor", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("MarkupString", StringComparison.Ordinal))
            .ToList();
        offenders.ShouldBeEmpty("render user content only through <Markdown>");
    }

    [Fact]
    public void Modules_write_only_to_tables_they_own()
    {
        var mutation = new Regex(@"\bdb\.(?<set>\w+)\s*\.(Add|AddRange|Remove|RemoveRange|Update)\(");
        var bulk = new Regex(@"\bdb\.(?<set>\w+)\b[^;]*?\.Execute(Update|Delete)Async", RegexOptions.Singleline);
        var violations = new List<string>();

        foreach (var (module, owned) in WriteOwnership)
        {
            var dir = Path.Combine(RepoRoot, "src", "MentorPath.Application", module);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                         .Where(f => !IsBuildOutput(f)))
            {
                var text = File.ReadAllText(file);
                foreach (var set in mutation.Matches(text).Concat(bulk.Matches(text)).Select(m => m.Groups["set"].Value))
                {
                    if (!owned.Contains(set))
                        violations.Add($"{module} writes {set} ({Path.GetFileName(file)})");
                }
            }
        }

        violations.Distinct().ShouldBeEmpty("write ownership matrix in docs/DOMAIN_RULES.md");
    }

    [Fact]
    public void Operator_services_are_used_only_by_the_CLI()
    {
        var offenders = SourceFiles("src")
            .Where(f => !OperatorServiceAllowedDirs.Any(d => f.Replace('\\', '/').Contains(d, StringComparison.Ordinal)))
            .Where(f => OperatorServices.Any(s => Regex.IsMatch(File.ReadAllText(f), $@"\b{s}\b")))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        offenders.ShouldBeEmpty("actorless operator services must not be reachable from pages, endpoints or other modules");
    }

    private static bool HasImplementation(Type contract) =>
        ApplicationAssembly.GetTypes().Any(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t));

    private static IEnumerable<string> SourceFiles(string relativeDir) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, relativeDir), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !IsBuildOutput(f));

    private static bool IsBuildOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/obj/", StringComparison.Ordinal) || p.Contains("/bin/", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MentorPath.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("MentorPath.slnx not found above the test output directory.");
    }
}
