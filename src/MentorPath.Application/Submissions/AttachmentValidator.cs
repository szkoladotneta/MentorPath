namespace MentorPath.Application.Submissions;

/// <summary>Validates uploaded attachments by size, extension, content type and file signature (spec §8.2).</summary>
public static class AttachmentValidator
{
    private static readonly Dictionary<string, string[]> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".txt"] = ["text/plain"],
        [".md"] = ["text/markdown", "text/plain", "text/x-markdown", "application/octet-stream"],
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
    };

    public static string? Validate(string fileName, string contentType, ReadOnlySpan<byte> content)
    {
        if (content.Length == 0) return "Plik jest pusty.";
        if (content.Length > Common.InputLimits.MaxAttachmentBytes) return "Plik może mieć maksymalnie 10 MB.";
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255) return "Nieprawidłowa nazwa pliku.";

        var ext = Path.GetExtension(fileName);
        if (!AllowedContentTypes.TryGetValue(ext, out var types)) return "Dozwolone formaty: PDF, TXT, MD, PNG, JPG.";
        var baseType = contentType.Split(';')[0].Trim();
        if (!types.Contains(baseType, StringComparer.OrdinalIgnoreCase)) return "Typ pliku nie zgadza się z rozszerzeniem.";

        var ok = ext.ToLowerInvariant() switch
        {
            ".pdf" => content.StartsWith("%PDF-"u8),
            ".png" => content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".jpg" or ".jpeg" => content.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            _ => IsUtf8Text(content),
        };
        return ok ? null : "Zawartość pliku nie odpowiada jego formatowi.";
    }

    public static string CanonicalContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".md" => "text/markdown",
        _ => "text/plain",
    };

    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '\'', '\\', '/']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        return cleaned.Length > 255 ? cleaned[^255..] : cleaned;
    }

    private static bool IsUtf8Text(ReadOnlySpan<byte> content)
    {
        if (content.IndexOf((byte)0) >= 0) return false;
        try
        {
            _ = new System.Text.UTF8Encoding(false, true).GetCharCount(content);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            return false;
        }
    }
}
