using MentorPath.Web.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MentorPath.IntegrationTests.Mcp;

/// <summary>Bearer tokens for the /mcp endpoint (D-64). No database needed; the real pipeline is covered by <see cref="McpHttpTests"/>.</summary>
public sealed class McpTokensTests
{
    /// <summary>64 bytes, so that a token signed with HS512 and the same key is rejected by the algorithm allow-list, not by key size.</summary>
    public static readonly string Key = McpTokens.KeyPrefix + Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)(i * 37 + 11)).ToArray());
    public static readonly string OtherKey = McpTokens.KeyPrefix + Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i * 53 + 7)).ToArray());
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Token_carries_user_id_and_security_stamp_and_validates_with_the_same_key()
    {
        var userId = Guid.NewGuid();
        var token = McpTokens.Create(userId, "stamp-1", McpTokens.DefaultAudience, TimeSpan.FromDays(90), Key, Clock);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(Key));

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
        result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)!.Value.ShouldBe(userId.ToString());
        result.ClaimsIdentity.FindFirst(McpTokens.SecurityStampClaim)!.Value.ShouldBe("stamp-1");
        result.SecurityToken.ShouldBeOfType<JsonWebToken>().ValidTo.ShouldBe(Clock.GetUtcNow().UtcDateTime.AddDays(90));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = McpTokens.Create(Guid.NewGuid(), "stamp", McpTokens.DefaultAudience, TimeSpan.FromDays(1), Key, Clock);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters(OtherKey));

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("too-short")]                                                 // no tag
    [InlineData("unit-test-signing-key-with-at-least-32-bytes")]              // a passphrase
    [InlineData("mentorpathmcpsigningkeymentorpathmcpsigningkeyAB")]          // a passphrase that happens to be valid Base64 of 36 bytes
    [InlineData("mpk1:not-base64!")]                                          // tagged, not Base64
    [InlineData("mpk1:AAAAAAAAAAAAAAAAAAAAAA==")]                              // tagged, 16 bytes
    [InlineData("mpk1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]          // tagged, 32 bytes, all zero
    public void Keys_not_generated_by_mcp_key_are_refused(string key)
    {
        Should.Throw<InvalidOperationException>(() => McpTokens.SecurityKey(key)).Message.ShouldContain("mcp-key");
    }

    [Fact]
    public void Generated_keys_are_accepted_and_unique()
    {
        var first = McpTokens.GenerateKey();
        var second = McpTokens.GenerateKey();

        first.ShouldStartWith(McpTokens.KeyPrefix);
        first.ShouldNotBe(second);
        McpTokens.SecurityKey(first).KeySize.ShouldBe(McpTokens.MinKeyBytes * 8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public void Lifetimes_outside_one_second_to_ninety_days_are_refused(int days)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => McpTokens.Create(Guid.NewGuid(), "stamp", McpTokens.DefaultAudience, TimeSpan.FromDays(days), Key, Clock));
    }

    [Fact]
    public void Audience_is_the_configured_value_then_the_public_base_url_then_the_default()
    {
        static string Resolve(params (string Key, string Value)[] pairs) => McpTokens.ResolveAudience(
            new ConfigurationBuilder().AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value)).Build());

        Resolve(("Mcp:Audience", "mcp-prod"), ("App:PublicBaseUrl", "https://x/")).ShouldBe("mcp-prod");
        Resolve(("App:PublicBaseUrl", "https://mentorpath.example/")).ShouldBe("https://mentorpath.example");
        Resolve().ShouldBe(McpTokens.DefaultAudience);
    }

    private static TokenValidationParameters Parameters(string key) => new()
    {
        ValidIssuer = McpTokens.Issuer,
        ValidAudience = McpTokens.DefaultAudience,
        IssuerSigningKey = McpTokens.SecurityKey(key),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = false, // the fake clock is not the validator's clock
    };
}
