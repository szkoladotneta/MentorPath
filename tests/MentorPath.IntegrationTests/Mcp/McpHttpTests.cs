using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Mcp;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MentorPath.IntegrationTests.Mcp;

/// <summary>/mcp through the real request pipeline (D-64): bearer-only, instance-bound tokens that die with the account.</summary>
public sealed class McpHttpTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private const string Audience = "http://localhost"; // WebAppFactory sets App:PublicBaseUrl to this, so it becomes the audience
    private static readonly object InitParams = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "tests", version = "1" } };
    private WebAppFactory _withKey = default!;
    private WebAppFactory _withoutKey = default!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _withKey = new WebAppFactory(Database, new Dictionary<string, string?> { ["Mcp:SigningKey"] = McpTokensTests.Key });
        _withoutKey = new WebAppFactory(Database);
    }

    public override async Task DisposeAsyncCore()
    {
        await _withKey.DisposeAsync();
        await _withoutKey.DisposeAsync();
    }

    [Fact]
    public async Task Without_a_signing_key_the_endpoint_does_not_exist()
    {
        using var client = _withoutKey.Client();
        var response = await client.SendAsync(JsonRpc("initialize", InitParams));
        // Nothing is mapped at /mcp, so the request falls through to the Razor Components catch-all (405 for POST, 404 for GET).
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("jsonrpc");
    }

    [Fact]
    public async Task Only_a_valid_bearer_token_of_a_mentor_authenticates()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        using var client = _withKey.Client();
        var threeDaysAgo = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(-3));

        (await StatusAsync(client, null)).ShouldBe(HttpStatusCode.Unauthorized, "no Authorization header");
        (await StatusAsync(client, await TokenAsync(mentor.UserId, TimeSpan.FromDays(1), clock: threeDaysAgo))).ShouldBe(HttpStatusCode.Unauthorized, "expired");
        (await StatusAsync(client, await TokenAsync(mentor.UserId, key: McpTokensTests.OtherKey))).ShouldBe(HttpStatusCode.Unauthorized, "another key");
        (await StatusAsync(client, await TokenAsync(mentor.UserId, audience: "https://another.instance"))).ShouldBe(HttpStatusCode.Unauthorized, "another instance's audience");
        (await StatusAsync(client, Crafted(mentor.UserId, issuer: "someone-else"))).ShouldBe(HttpStatusCode.Unauthorized, "another issuer");
        (await StatusAsync(client, Crafted(mentor.UserId, algorithm: SecurityAlgorithms.HmacSha512))).ShouldBe(HttpStatusCode.Unauthorized, "another algorithm");
        (await StatusAsync(client, Crafted(mentor.UserId, stamp: "not-the-current-stamp"))).ShouldBe(HttpStatusCode.Unauthorized, "another security stamp");
        (await StatusAsync(client, await TokenAsync(mentee.UserId))).ShouldBe(HttpStatusCode.Unauthorized, "a mentee's account");
        (await StatusAsync(client, await TokenAsync(Guid.NewGuid()))).ShouldBe(HttpStatusCode.Unauthorized, "unknown user");
        (await StatusAsync(client, await TokenAsync(mentor.UserId))).ShouldBe(HttpStatusCode.OK, "valid mentor token");

        using var cookieClient = _withKey.Client();
        await WebAppFactory.LoginAsync(cookieClient, mentor.Email, TestDataBuilder.Password);
        (await StatusAsync(cookieClient, null)).ShouldBe(HttpStatusCode.Unauthorized, "the UI cookie alone never authenticates /mcp");
    }

    [Fact]
    public async Task Token_dies_with_a_security_stamp_refresh_a_lockout_or_the_lost_mentor_role()
    {
        var mentor = await Data.MentorAsync();
        using var client = _withKey.Client();
        var users = App.Get<UserManager<AppUser>>();
        var user = (await users.FindByIdAsync(mentor.UserId.ToString()))!;

        var token = await TokenAsync(mentor.UserId);
        (await StatusAsync(client, token)).ShouldBe(HttpStatusCode.OK);
        (await users.UpdateSecurityStampAsync(user)).Succeeded.ShouldBeTrue(); // what a password reset does
        (await StatusAsync(client, token)).ShouldBe(HttpStatusCode.Unauthorized, "stale security stamp");

        var fresh = await TokenAsync(mentor.UserId);
        (await StatusAsync(client, fresh)).ShouldBe(HttpStatusCode.OK);
        (await users.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
        (await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1))).Succeeded.ShouldBeTrue();
        (await StatusAsync(client, fresh)).ShouldBe(HttpStatusCode.Unauthorized, "locked out");
        (await users.SetLockoutEndDateAsync(user, null)).Succeeded.ShouldBeTrue();
        (await StatusAsync(client, fresh)).ShouldBe(HttpStatusCode.OK);

        (await users.RemoveFromRoleAsync(user, AppRoles.Mentor)).Succeeded.ShouldBeTrue();
        (await StatusAsync(client, fresh)).ShouldBe(HttpStatusCode.Unauthorized, "no longer a mentor");
    }

    [Fact]
    public async Task Requests_are_rate_limited_per_mentor()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        using var client = _withKey.Client(); // one client (one IP) for both mentors: the partition must be the mentor, not the address
        var tokenA = await TokenAsync(a.UserId);
        var tokenB = await TokenAsync(b.UserId);

        for (var i = 0; i < McpComposition.RateLimitPerMinute; i++)
            (await StatusAsync(client, tokenA)).ShouldBe(HttpStatusCode.OK, $"request {i + 1}");
        (await StatusAsync(client, tokenA)).ShouldBe(HttpStatusCode.TooManyRequests, "mentor A over the limit");
        (await StatusAsync(client, tokenB)).ShouldBe(HttpStatusCode.OK, "mentor B has their own budget");
    }

    [Fact]
    public async Task A_valid_token_sees_only_that_mentors_mentees()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var menteeA = await Data.MenteeAsync(a);
        var menteeB = await Data.MenteeAsync(b);
        using var client = _withKey.Client();
        var token = await TokenAsync(a.UserId);

        var init = await client.SendAsync(JsonRpc("initialize", InitParams, token));
        init.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = init.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : null;
        if (session is not null)
            await client.SendAsync(JsonRpc("notifications/initialized", null, token, session, notification: true));

        var call = await client.SendAsync(JsonRpc("tools/call", new { name = "list_mentees", arguments = new { } }, token, session));
        call.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await ReadResponseAsync(call)).GetProperty("result").ToString();
        result.ShouldContain(menteeA.MentorshipId.ToString());
        result.ShouldNotContain(menteeB.MentorshipId.ToString());
        result.ShouldNotContain(menteeA.Email);
    }

    [Fact]
    public async Task Review_tools_are_discoverable_but_refuse_calls_when_the_operator_gate_is_off()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        using var client = _withKey.Client();
        var token = await TokenAsync(mentor.UserId);
        using var listed = await client.SendAsync(JsonRpc("tools/list", new { }, token));
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tools = (await ReadResponseAsync(listed)).GetProperty("result").GetProperty("tools");
        tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ShouldContain("get_review_context");
        using var call = await client.SendAsync(JsonRpc("tools/call",
            new { name = "get_review_context", arguments = new { mentorshipId = mentee.MentorshipId, submissionId = submission.SubmissionId } }, token));
        call.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await ReadResponseAsync(call)).GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        result.ToString().ShouldNotContain("Modularny monolit");
    }

    [Fact]
    public async Task HTTP_review_context_and_draft_require_current_consent_and_cannot_publish()
    {
        const string notice = "Synthetic approved HTTP test client.";
        var enabledSettings = new Dictionary<string, string?>
        {
            ["Mcp:SigningKey"] = McpTokensTests.Key,
            ["Mcp:Reviews:Enabled"] = "true", ["Mcp:Reviews:ProviderNotice"] = notice,
        };
        await using var enabledApp = await TestApp.CreateAsync(Database, enabledSettings);
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var consentService = enabledApp.Get<IMentorshipReviewConsentService>();
        (await consentService.GrantAsync(mentee.UserId,
            new(mentee.MentorshipId, new McpReviewOptions { ProviderNotice = notice }.DisclosureVersion, null), default)).ShouldSucceed();
        await using var server = new WebAppFactory(Database, enabledSettings);
        using var client = server.Client();
        var token = await TokenAsync(mentor.UserId);
        using var context = await client.SendAsync(JsonRpc("tools/call", new
        {
            name = "get_review_context",
            arguments = new { mentorshipId = mentee.MentorshipId, submissionId = submission.SubmissionId },
        }, token));
        context.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = (await ReadResponseAsync(context)).GetProperty("result");
        read.TryGetProperty("isError", out var readError);
        (readError.ValueKind == JsonValueKind.True).ShouldBeFalse(read.ToString());
        read.ToString().ShouldContain("Modularny monolit");
        read.ToString().ShouldNotContain(mentee.Email);
        using var save = await client.SendAsync(JsonRpc("tools/call", new
        {
            name = "save_review_draft",
            arguments = new { mentorshipId = mentee.MentorshipId, submissionId = submission.SubmissionId, strengthsMarkdown = "Synthetic HTTP draft" },
        }, token));
        var saved = (await ReadResponseAsync(save)).GetProperty("result");
        saved.TryGetProperty("isError", out var saveError);
        (saveError.ValueKind == JsonValueKind.True).ShouldBeFalse(saved.ToString());
        saved.ToString().ShouldContain("not published");
        (await enabledApp.Get<IReviewDraftService>().GetAsync(mentor.UserId, submission.SubmissionId, default)).ShouldSucceed()!
            .Content.StrengthsMarkdown.ShouldBe("Synthetic HTTP draft");
        var consent = (await consentService.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        (await consentService.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, consent.Version!.Value), default)).ShouldSucceed();
        using var revoked = await client.SendAsync(JsonRpc("tools/call", new
        {
            name = "get_review_draft",
            arguments = new { mentorshipId = mentee.MentorshipId, submissionId = submission.SubmissionId },
        }, token));
        (await ReadResponseAsync(revoked)).GetProperty("result").GetProperty("isError").GetBoolean().ShouldBeTrue();
        using var deniedPublication = await client.SendAsync(JsonRpc("tools/call",
            new { name = "publish_review", arguments = new { submissionId = submission.SubmissionId } }, token));
        var notATool = await ReadResponseAsync(deniedPublication);
        (notATool.TryGetProperty("error", out _) ||
            (notATool.GetProperty("result").TryGetProperty("isError", out var publishError) && publishError.GetBoolean())).ShouldBeTrue();
    }

    private async Task<string> TokenAsync(Guid userId, TimeSpan? lifetime = null, string? key = null, string? audience = null, TimeProvider? clock = null)
    {
        var users = App.Get<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        var stamp = user is null ? "no-such-user" : await users.GetSecurityStampAsync(user);
        return McpTokens.Create(userId, stamp, audience ?? Audience, lifetime ?? TimeSpan.FromDays(1), key ?? McpTokensTests.Key, clock ?? TimeProvider.System);
    }

    /// <summary>A token the issuer would never mint: wrong issuer, algorithm or stamp, but otherwise well-formed and signed with the real key.</summary>
    private static string Crafted(Guid userId, string issuer = McpTokens.Issuer, string algorithm = SecurityAlgorithms.HmacSha256, string stamp = "x")
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(McpTokens.SecurityStampClaim, stamp)]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(1),
            SigningCredentials = new SigningCredentials(McpTokens.SecurityKey(McpTokensTests.Key), algorithm),
        });
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string? bearer) =>
        (await client.SendAsync(JsonRpc("initialize", InitParams, bearer))).StatusCode;

    private static HttpRequestMessage JsonRpc(string method, object? @params, string? bearer = null, string? sessionId = null, bool notification = false)
    {
        var body = notification
            ? (object)new { jsonrpc = "2.0", method, @params = @params ?? new { } }
            : new { jsonrpc = "2.0", id = 1, method, @params = @params ?? new { } };
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (sessionId is not null) request.Headers.Add("Mcp-Session-Id", sessionId);
        return request;
    }

    /// <summary>The SDK answers with JSON or with one SSE event per message; the response to our request is the one carrying our id.</summary>
    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
            return JsonDocument.Parse(text).RootElement;
        foreach (var line in text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)))
        {
            var element = JsonDocument.Parse(line[5..].Trim()).RootElement;
            if (element.TryGetProperty("id", out _)) return element;
        }
        throw new InvalidOperationException("No JSON-RPC response in the event stream: " + text);
    }
}
