using MentorPath.Web.Mcp;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MentorPath.IntegrationTests.Mcp;

/// <summary>Bearer tokens for the /mcp endpoint (D-64). No database needed.</summary>
public sealed class McpTokensTests
{
    private const string Key = "unit-test-signing-key-with-at-least-32-bytes";
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Token_carries_the_user_id_as_subject_and_validates_with_the_same_key()
    {
        var userId = Guid.NewGuid();
        var token = McpTokens.Create(userId, TimeSpan.FromDays(365), Key, Clock);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(Key));

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)!.Value.ShouldBe(userId.ToString());
        result.SecurityToken.ShouldBeOfType<JsonWebToken>().ValidTo.ShouldBe(Clock.GetUtcNow().UtcDateTime.AddDays(365));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = McpTokens.Create(Guid.NewGuid(), TimeSpan.FromDays(1), Key, Clock);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters("another-signing-key-with-at-least-32-bytes"));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Short_signing_keys_are_refused()
    {
        Should.Throw<InvalidOperationException>(() => { McpTokens.Create(Guid.NewGuid(), TimeSpan.FromDays(1), "too-short", Clock); });
    }

    private static TokenValidationParameters Parameters(string key) => new()
    {
        ValidIssuer = McpTokens.Issuer,
        ValidAudience = McpTokens.Audience,
        IssuerSigningKey = McpTokens.SecurityKey(key),
        ValidateLifetime = false, // the fake clock is not the validator's clock
    };
}
