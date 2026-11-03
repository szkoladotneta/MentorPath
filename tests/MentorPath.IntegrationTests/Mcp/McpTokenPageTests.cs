using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MentorPath.Data;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Cli;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MentorPath.IntegrationTests.Mcp;

public sealed partial class McpTokenPageTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private const string PagePath = "/Account/Manage/McpToken";
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
    public async Task Mentor_can_issue_a_token_for_the_signed_in_account_once_and_use_it_with_mcp()
    {
        var mentor = await Data.MentorAsync();
        var otherMentor = await Data.MentorAsync();
        using var client = _withKey.Client();
        await WebAppFactory.LoginAsync(client, mentor.Email, TestDataBuilder.Password);

        var page = await client.GetStringAsync(PagePath);
        page.ShouldContain("value=\"30\"");
        var pageText = WebUtility.HtmlDecode(page);
        pageText.ShouldContain("Token jest poufny");
        pageText.ShouldContain("uprawnień tego konta");
        pageText.ShouldContain("Authorization");
        pageText.ShouldContain("Bearer");
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var response = await PostTokenPageAsync(client, lifetimeDays: null, forgedUserId: otherMentor.UserId.ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var html = await response.Content.ReadAsStringAsync();
        var token = ExtractToken(html);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Subject.ShouldBe(mentor.UserId.ToString());
        jwt.ValidTo.ShouldBeInRange(now.AddDays(30).AddMinutes(-1), now.AddDays(30).AddMinutes(1));

        using var mcpResponse = await client.SendAsync(InitializeRequest(token));
        mcpResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var reloaded = await client.GetStringAsync(PagePath);
        reloaded.ShouldNotContain(token);

        var tokenPersistence = await App.Db(context => Task.FromResult(context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => $"{entity.GetTableName()}.{property.Name}"))
            .Where(name => name.Contains("McpToken", StringComparison.OrdinalIgnoreCase))
            .ToList()));
        tokenPersistence.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("90", 90)]
    public async Task Mentor_can_issue_tokens_at_the_lifetime_boundaries(string days, int expectedDays)
    {
        var mentor = await Data.MentorAsync();
        using var client = _withKey.Client();
        await WebAppFactory.LoginAsync(client, mentor.Email, TestDataBuilder.Password);

        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var response = await PostTokenPageAsync(client, days);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var token = ExtractToken(await response.Content.ReadAsStringAsync());
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.ValidTo.ShouldBeInRange(now.AddDays(expectedDays).AddMinutes(-1), now.AddDays(expectedDays).AddMinutes(1));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("91")]
    [InlineData("not-a-number")]
    public async Task Invalid_lifetime_does_not_issue_a_token(string days)
    {
        var mentor = await Data.MentorAsync();
        using var client = _withKey.Client();
        await WebAppFactory.LoginAsync(client, mentor.Email, TestDataBuilder.Password);

        var response = await PostTokenPageAsync(client, days);

        var html = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, html);
        html.ShouldNotContain("issued-token");
        html.ShouldContain("role=\"alert\"");
        if (int.TryParse(days, out _)) WebUtility.HtmlDecode(html).ShouldContain("Ważność tokenu musi wynosić od 1 do 90 dni.");
    }

    [Fact]
    public async Task Missing_or_invalid_signing_key_shows_an_error_without_issuing_a_token()
    {
        var mentor = await Data.MentorAsync();
        using var client = _withoutKey.Client();
        await WebAppFactory.LoginAsync(client, mentor.Email, TestDataBuilder.Password);

        var missingKeyResponse = await PostTokenPageAsync(client, "30");
        var missingKeyHtml = await missingKeyResponse.Content.ReadAsStringAsync();
        WebUtility.HtmlDecode(missingKeyHtml).ShouldContain("Wydawanie tokenów MCP nie jest skonfigurowane.");
        missingKeyHtml.ShouldNotContain("issued-token");

        var configuration = _withoutKey.Services.GetRequiredService<IConfiguration>();
        configuration["Mcp:SigningKey"] = "invalid-key";
        var invalidKeyResponse = await PostTokenPageAsync(client, "30");
        var invalidKeyHtml = await invalidKeyResponse.Content.ReadAsStringAsync();
        WebUtility.HtmlDecode(invalidKeyHtml).ShouldContain("Konfiguracja podpisywania tokenów MCP jest nieprawidłowa.");
        invalidKeyHtml.ShouldNotContain("issued-token");
    }

    [Fact]
    public async Task Only_mentors_can_open_the_token_page()
    {
        using var anonymousClient = _withKey.Client();
        var anonymousResponse = await anonymousClient.GetAsync(PagePath);
        anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        using var menteeClient = _withKey.Client();
        await WebAppFactory.LoginAsync(menteeClient, mentee.Email, TestDataBuilder.Password);

        var menteeResponse = await menteeClient.GetAsync(PagePath);
        menteeResponse.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        menteeResponse.Headers.Location!.ToString().ShouldContain("/Account/AccessDenied");
    }

    [Fact]
    public async Task Issuing_a_token_does_not_write_it_to_application_logs()
    {
        var mentor = await Data.MentorAsync();
        using var logs = new CapturingLoggerProvider();
        using var factory = new WebAppFactory(Database, new Dictionary<string, string?> { ["Mcp:SigningKey"] = McpTokensTests.Key }, logs);
        using var client = factory.Client();
        await WebAppFactory.LoginAsync(client, mentor.Email, TestDataBuilder.Password);

        var response = await PostTokenPageAsync(client, "30");
        var token = ExtractToken(await response.Content.ReadAsStringAsync());

        logs.Messages.ShouldNotBeEmpty();
        logs.Messages.ShouldAllBe(message => !message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public void Existing_mcp_token_cli_command_remains_registered()
    {
        CliRunner.IsCliCommand(["mcp-token"]).ShouldBeTrue();
    }

    private async Task<HttpResponseMessage> PostTokenPageAsync(HttpClient client, string? lifetimeDays, string? forgedUserId = null)
    {
        var html = await client.GetStringAsync(PagePath);
        var formNameIndex = html.IndexOf("mcp-token", StringComparison.Ordinal);
        var antiforgeryToken = WebUtility.HtmlDecode(AntiforgeryInput().Match(html).Groups[1].Value);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiforgeryToken,
            ["_handler"] = "mcp-token",
        };
        if (lifetimeDays is not null) fields["Input.LifetimeDays"] = lifetimeDays;
        if (forgedUserId is not null) fields["Input.UserId"] = forgedUserId;
        return await client.PostAsync(PagePath, new FormUrlEncodedContent(fields));
    }

    private static string ExtractToken(string html)
    {
        var match = IssuedToken().Match(html);
        match.Success.ShouldBeTrue("the generated token must be shown in the one-time response");
        return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
    }

    private static HttpRequestMessage InitializeRequest(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "tests", version = "1" } },
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();

    [GeneratedRegex("<textarea[^>]*id=\"issued-token\"[^>]*>([^<]+)</textarea>")]
    private static partial Regex IssuedToken();

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
