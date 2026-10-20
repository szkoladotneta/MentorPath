---
applyTo: "src/MentorPath.Web/Components/**,src/MentorPath.Web/wwwroot/*.css"
---

# Razor UI

- Components call application services and use DTOs only. Never inject `AppDbContext` or `IDbContextFactory<AppDbContext>` (enforced by `ArchitectureTests`).
- Every page has an `[Authorize]` attribute with the right role (enforced by `ArchitectureTests`). Authorization still happens in the service; the attribute is not enough.
- Get the current user id from the authentication state, never from a route or query parameter. Route ids are inputs to be checked by the service, not permissions.
- Handle all states: loading, empty, error (use `PageState`/`ErrorAlert` from `Components/Shared`; a `NotFound` result renders the not-found state).
- Render user-supplied Markdown only with the `<Markdown>` component. Never use `MarkupString` elsewhere.
- UI strings are in Polish and use the shared components in `Components/Shared`.
- Forms validate on the server through the service result; client-side validation is only a convenience.

## Layout and design system

`DESIGN.md` is the source of truth (tokens, typography, shell, craft floor; decision D-44). Read it before building or changing any page. Do not re-decide layout per page; compose these pieces.

**Page skeleton.** Every page is `<PageTitle>` → `<Page Width="wide|reading|narrow">` → `<PageHeader Title Subtitle Crumbs Actions>` → `<PageState Loading NotFound>` → content. Copy `Pages/Mentor/Reviews/ReviewQueue.razor` as the reference.

- Width: `wide` (default) for lists, dashboards and split views; `reading` for long content (assignment, markdown); `narrow` for account and single-purpose forms. Do not set `max-width` yourself.
- Breadcrumbs (`Crumb`) on every page deeper than a top-level nav item; the last crumb is the current page. Primary action goes in `PageHeader` `Actions`, one primary button per page.
- Group content in `<Section Title>`; never nest a `Section`/`.card` inside another. Lists use `DataList` + `DataRow` (`Flush` section for edge-to-edge lists), numbers use `StatTile`, main + side content uses `SplitView` (stacks on mobile), sibling views use `Tabs`. Reach for a new shared component only when none of these fit, and add it to `Components/Shared` and `DESIGN.md`.
- Empty state: `EmptyState` with an icon and, when there is a next step, an `Action`. Loading and not-found come from `PageState`. Errors: `ErrorAlert`.
- Status is shown with `<Badge Value="...">` (word + dot + tone), never colour alone. Icons are `<Icon Name>`; no emoji or glyph icons.
- Navigation entries live in `AppRail`; add a new top-level page there for its role, with an icon. Do not build page-local navigation.

**Styling.** Use design tokens (`--mp-*` from `wwwroot/tokens.css`) and Bootstrap utilities mapped onto them; no hard-coded hex colours, font sizes or pixel spacing. No inline `style` for layout: add a class to `components.css` (shell chrome goes in `shell.css`). Prefer the 8pt spacing scale (`--mp-space-*`). Do not add a CSS framework, icon font or web font from a CDN.

**Forms.** Every control has a `<label for>` and a matching `id`; show server errors with `FieldError`/`ErrorAlert`; narrow inputs use `field-narrow`/`field-medium`; disable the submit button while busy.

**Accessibility and responsiveness.** Keep visible focus, touch targets of at least 40px on mobile, and no horizontal overflow at 360, 768 and 1280px. Check these widths for any new or changed page. Respect `prefers-reduced-motion`. Interactive elements are real `<a>`/`<button>` elements.

**Craft floor (do not reintroduce).** No eyebrow text above headings, coloured side borders, nested cards, hero-metric templates, gradient text, or hard offset shadows. Light theme only.

**Impeccable.** The Impeccable design hook runs on UI edits; fix its findings instead of suppressing them. For non-trivial new pages use `/impeccable layout` or `/impeccable polish` and update `DESIGN.md` if the system changes.
