using Ganss.Xss;
using Markdig;

namespace MentorPath.Application.Common.Markdown;

public interface IMarkdownRenderer
{
    string RenderToSafeHtml(string? markdown);
}

internal sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .Build();

    private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

    public string RenderToSafeHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var html = Markdig.Markdown.ToHtml(markdown, Pipeline);
        return _sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedAttributes.Remove("style");
        s.AllowedTags.Remove("img");
        s.AllowedTags.Remove("form");
        s.AllowedTags.Remove("input");
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                a.SetAttribute("rel", "noopener noreferrer nofollow");
                a.SetAttribute("target", "_blank");
            }
        };
        return s;
    }
}
