using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MentorPath.IntegrationTests.Infrastructure;

public sealed partial class WebAppFactory(PostgresFixture db) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
        builder.UseSetting("App:DisableHttpsRedirection", "true");
        builder.UseSetting("App:PublicBaseUrl", "http://localhost");
        builder.UseSetting("Storage:Provider", "InMemory");
        builder.UseSetting("Storage:EnsureContainerOnStartup", "false");
        builder.UseSetting("Ai:Provider", "Fake");
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryInput().Match(page).Groups[1].Value;
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
            ["_handler"] = "login",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, $"login failed for {email}");
    }

    public static async Task<(string Header, string Token)> AntiforgeryAsync(HttpClient client)
    {
        var json = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/antiforgery-token");
        return (json!["headerName"], json["requestToken"]);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryInput();
}
