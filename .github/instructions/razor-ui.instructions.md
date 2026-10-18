---
applyTo: "src/MentorPath.Web/Components/**"
---

# Razor UI

- Components call application services and use DTOs only. Never inject `AppDbContext` or `IDbContextFactory<AppDbContext>` (enforced by `ArchitectureTests`).
- Every page has an `[Authorize]` attribute with the right role (enforced by `ArchitectureTests`). Authorization still happens in the service; the attribute is not enough.
- Get the current user id from the authentication state, never from a route or query parameter. Route ids are inputs to be checked by the service, not permissions.
- Handle all states: loading, empty, error (use `PageState`/`ErrorAlert` from `Components/Shared`; a `NotFound` result renders the not-found state).
- Render user-supplied Markdown only with the `<Markdown>` component. Never use `MarkupString` elsewhere.
- UI strings are in Polish and use the shared components in `Components/Shared`. Keep layout and styling consistent with the existing pages.
- Forms validate on the server through the service result; client-side validation is only a convenience.
