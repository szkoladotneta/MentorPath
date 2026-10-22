using MentorPath.Application.Catalog;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.Catalog;

public sealed class TopicResourceValidatorTests
{
    [Fact]
    public void Validates_the_documented_field_limits_and_https_urls()
    {
        var resource = new TopicResourceInput(
            new string('t', 200),
            "https://example.com/" + new string('u', 1980),
            TopicResourceKind.Video,
            new string('d', 1000));

        TopicResourceValidator.Validate([resource]).HasErrors.ShouldBeFalse();
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:password@example.com")]
    [InlineData("/relative/path")]
    public void Rejects_non_absolute_https_urls_and_credentials(string url)
    {
        var errors = TopicResourceValidator.Validate(
            [new TopicResourceInput("Materiał", url, TopicResourceKind.Article, null)]).ToDictionary();

        errors.ShouldContainKey("Resources[0].Url");
    }

    [Fact]
    public void Rejects_oversized_title_description_invalid_kind_and_html_description()
    {
        var errors = TopicResourceValidator.Validate(
        [
            new TopicResourceInput(new string('t', 201), "https://example.com", (TopicResourceKind)99, new string('d', 1001)),
            new TopicResourceInput("Materiał", "https://example.com", TopicResourceKind.Article, "<em>HTML</em>"),
        ]).ToDictionary();

        errors.Keys.ShouldContain("Resources[0].Title");
        errors.Keys.ShouldContain("Resources[0].Kind");
        errors.Keys.ShouldContain("Resources[0].Description");
        errors.Keys.ShouldContain("Resources[1].Description");
    }
}
