using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace MentorPath.Web.Components.Shared;

public sealed record TopicVideoEmbed(string Provider, string EmbedUrl);

public static partial class TopicVideoEmbedParser
{
    public static TopicVideoEmbed? Parse(string? sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0)
            return null;

        var host = uri.IdnHost.ToLowerInvariant();
        if (host is "youtube.com" or "www.youtube.com" or "youtu.be")
            return ParseYoutube(host, uri);
        if (host is "vimeo.com" or "www.vimeo.com" or "player.vimeo.com")
            return ParseVimeo(host, uri);
        return null;
    }

    private static TopicVideoEmbed? ParseYoutube(string host, Uri uri)
    {
        string? id = null;
        if (host == "youtu.be")
        {
            var segments = PathSegments(uri);
            if (segments.Length == 1) id = segments[0];
        }
        else
        {
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Equals("/watch", StringComparison.Ordinal))
            {
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var values) && values.Count == 1) id = values[0];
            }
            else
            {
                var segments = PathSegments(uri);
                if (segments.Length == 2
                    && (segments[0] is "embed" or "shorts"))
                    id = segments[1];
            }
        }

        return IsYoutubeId(id)
            ? new TopicVideoEmbed("YouTube", $"https://www.youtube-nocookie.com/embed/{id}")
            : null;
    }

    private static TopicVideoEmbed? ParseVimeo(string host, Uri uri)
    {
        var segments = PathSegments(uri);
        string? id;
        string? pathHash = null;
        if (host == "player.vimeo.com")
        {
            if (segments.Length != 2 || !segments[0].Equals("video", StringComparison.OrdinalIgnoreCase)) return null;
            id = segments[1];
        }
        else
        {
            if (segments.Length is < 1 or > 2) return null;
            id = segments[0];
            if (segments.Length == 2) pathHash = segments[1];
        }

        if (!IsVimeoId(id)) return null;
        var query = QueryHelpers.ParseQuery(uri.Query);
        string? queryHash = null;
        if (query.TryGetValue("h", out var hashes))
        {
            if (hashes.Count != 1) return null;
            queryHash = hashes[0];
        }

        if (pathHash is not null && queryHash is not null) return null;
        var hash = pathHash ?? queryHash;
        if (hash is not null && !VimeoHashRegex().IsMatch(hash)) return null;
        var embedUrl = $"https://player.vimeo.com/video/{id}?dnt=1";
        if (hash is not null) embedUrl += $"&h={Uri.EscapeDataString(hash)}";
        return new TopicVideoEmbed("Vimeo", embedUrl);
    }

    private static string[] PathSegments(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsYoutubeId(string? id) => id is not null && YoutubeIdRegex().IsMatch(id);

    private static bool IsVimeoId(string? id) => id is not null && VimeoIdRegex().IsMatch(id);

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex YoutubeIdRegex();

    [GeneratedRegex("^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex VimeoIdRegex();

    [GeneratedRegex("^[A-Za-z0-9]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex VimeoHashRegex();
}
