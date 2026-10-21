namespace MentorPath.Web.Components.Shared;

/// <summary>One breadcrumb step. The last crumb (or one without <see cref="Href"/>) is rendered as the current page.</summary>
public sealed record Crumb(string Text, string? Href = null);
