using MentorPath.Application.Common;
using MentorPath.Application.Common.Markdown;

namespace MentorPath.UnitTests.Common;

public sealed class SecurityHelpersTests
{
    private readonly IMarkdownRenderer _renderer = new MarkdownRenderer();

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://evil\"></iframe>")]
    public void Markdown_rendering_neutralizes_xss(string input)
    {
        var html = _renderer.RenderToSafeHtml(input).ToLowerInvariant();
        // Raw HTML must be escaped as text; no executable tags or javascript: links may survive.
        html.ShouldNotContain("<script");
        html.ShouldNotContain("<iframe");
        html.ShouldNotContain("<img");
        System.Text.RegularExpressions.Regex.IsMatch(html, "<a[^>]+href=\"javascript:").ShouldBeFalse();
    }

    [Fact]
    public void Markdown_renders_formatting_and_safe_links()
    {
        var html = _renderer.RenderToSafeHtml("**Bold** and [link](https://example.com)\n\n- item");
        html.ShouldContain("<strong>Bold</strong>");
        html.ShouldContain("rel=\"noopener noreferrer nofollow\"");
        html.ShouldContain("<li>item</li>");
    }

    [Fact]
    public void Tokens_are_random_and_hash_is_sha256_hex()
    {
        var a = Tokens.NewToken();
        var b = Tokens.NewToken();
        a.ShouldNotBe(b);
        a.Length.ShouldBeGreaterThanOrEqualTo(43);
        a.ShouldNotContain("+");
        a.ShouldNotContain("/");
        Tokens.Hash(a).Length.ShouldBe(64);
        Tokens.Hash(a).ShouldBe(Tokens.Hash(a));
    }

    [Theory]
    [InlineData("https://github.com/x", true)]
    [InlineData("http://gitlab.local/x", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://x", false)]
    [InlineData("not a url", false)]
    public void Only_http_urls_are_accepted(string url, bool ok) => FieldErrors.IsHttpUrl(url).ShouldBe(ok);

    [Fact]
    public void Email_masking_hides_local_part() => Text.MaskEmail("anna@example.com").ShouldBe("a***@example.com");
}
