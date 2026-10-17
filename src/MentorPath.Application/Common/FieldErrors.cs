using System.Text.RegularExpressions;

namespace MentorPath.Application.Common;

/// <summary>Small helper collecting field errors; keeps validators explicit without a validation framework.</summary>
public sealed partial class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }
        list.Add(message);
    }

    public void Required(string field, string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, $"Pole „{label}” jest wymagane.");
        else if (value.Length > maxLength) Add(field, $"Pole „{label}” może mieć maksymalnie {maxLength} znaków.");
    }

    public void Optional(string field, string? value, int maxLength, string label)
    {
        if (value is not null && value.Length > maxLength) Add(field, $"Pole „{label}” może mieć maksymalnie {maxLength} znaków.");
    }

    public void Range(string field, int? value, int min, int max, string label)
    {
        if (value is null || value < min || value > max) Add(field, $"Pole „{label}” musi mieć wartość od {min} do {max}.");
    }

    public void Confidence(string field, int value) => Range(field, value, 1, 5, "Pewność");

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

    public AppError ToError(string code) => Errors.Validation(code, ToDictionary());

    public static bool ContainsHtml(string? value) => value is not null && HtmlTag().IsMatch(value);

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z][^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTag();
}
