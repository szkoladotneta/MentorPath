using MentorPath.Web.Components.Shared;

namespace MentorPath.IntegrationTests.Presentation;

public sealed class TopicVideoEmbedParserTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&feature=share")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=tracking-value")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/embed/dQw4w9WgXcQ")]
    public void Youtube_forms_create_privacy_enhanced_embed_without_forwarding_parameters(string url)
    {
        TopicVideoEmbedParser.Parse(url).ShouldBe(
            new TopicVideoEmbed("YouTube", "https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ"));
    }

    [Theory]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("https://www.vimeo.com/123456789?share=copy")]
    [InlineData("https://player.vimeo.com/video/123456789")]
    public void Vimeo_forms_create_do_not_track_embed(string url)
    {
        TopicVideoEmbedParser.Parse(url).ShouldBe(
            new TopicVideoEmbed("Vimeo", "https://player.vimeo.com/video/123456789?dnt=1"));
    }

    [Theory]
    [InlineData("https://vimeo.com/123456789/Abc123")]
    [InlineData("https://vimeo.com/123456789?h=Abc123&share=copy")]
    [InlineData("https://player.vimeo.com/video/123456789?h=Abc123")]
    public void Vimeo_unlisted_hash_is_validated_and_preserved(string url)
    {
        TopicVideoEmbedParser.Parse(url).ShouldBe(
            new TopicVideoEmbed("Vimeo", "https://player.vimeo.com/video/123456789?dnt=1&h=Abc123"));
    }

    [Theory]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com.evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com@evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=short")]
    [InlineData("https://youtube.com/channel/dQw4w9WgXcQ")]
    [InlineData("https://vimeo.com/123abc")]
    [InlineData("https://vimeo.com/123456789/unsafe-hash!")]
    [InlineData("https://player.vimeo.com/video/123456789?h=first&h=second")]
    [InlineData("https://example.com/video/dQw4w9WgXcQ")]
    public void Unsupported_or_unsafe_video_urls_remain_link_only(string url) =>
        TopicVideoEmbedParser.Parse(url).ShouldBeNull();
}
