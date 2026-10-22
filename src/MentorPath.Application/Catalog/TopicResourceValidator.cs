using MentorPath.Application.Common;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Catalog;

internal static class TopicResourceValidator
{
    public static FieldErrors Validate(IReadOnlyList<TopicResourceInput>? resources)
    {
        var errors = new FieldErrors();
        if (resources is null)
        {
            errors.Add("Resources", "Lista materiałów jest wymagana.");
            return errors;
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            if (resource is null)
            {
                errors.Add($"Resources[{i}]", "Materiał jest wymagany.");
                continue;
            }

            var titleField = $"Resources[{i}].Title";
            var urlField = $"Resources[{i}].Url";
            var kindField = $"Resources[{i}].Kind";
            var descriptionField = $"Resources[{i}].Description";
            errors.Required(titleField, resource.Title, InputLimits.Title, "Tytuł");
            errors.Required(urlField, resource.Url, InputLimits.ExternalUrl, "Adres URL");
            errors.Optional(descriptionField, resource.Description, InputLimits.ShortText, "Opis");

            if (!string.IsNullOrWhiteSpace(resource.Url) && !IsSecureUrl(resource.Url.Trim()))
                errors.Add(urlField, "Adres musi być bezwzględnym adresem HTTPS bez danych logowania.");
            if (!Enum.IsDefined(resource.Kind))
                errors.Add(kindField, "Wybierz rodzaj materiału.");
            if (FieldErrors.ContainsHtml(resource.Description))
                errors.Add(descriptionField, "Opis musi być zwykłym tekstem.");
        }

        return errors;
    }

    private static bool IsSecureUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;
}
