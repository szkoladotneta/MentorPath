# MentorPath — invariants

These rules apply to every change. Build/test commands, repository map and workflow are in `AGENTS.md`. Convention tests in `tests/MentorPath.IntegrationTests/Isolation` enforce many of them; do not edit or weaken those tests to make a change pass.

## Authorization and tenant isolation

- Every public service method takes `Guid actorUserId` as its first parameter and checks access through `IWorkspaceAccessService` before reading or writing. An Identity role is not a permission; an id in the URL is not a permission.
- No access, or a resource that does not exist, returns `NotFound`. Never reveal that a foreign resource exists.
- `WorkspaceId` is never taken from a request, route, form or UI state. Derive it on the server.
- Cross-workspace isolation must be proven by an integration test (foreign mentor, foreign mentee, unknown user → `NotFound`).
- No global query filters. Authorization is explicit in each service.

## Data and layering

- Razor components use services and DTOs only; never `AppDbContext` or `IDbContextFactory`.
- Services use `IDbContextFactory<AppDbContext>`, one context per method, `AsNoTracking` for reads.
- A module writes only to the tables it owns (matrix in `docs/DOMAIN_RULES.md`, enforced by `ConventionTests`).
- DTOs for mentees never contain mentor-private data or correct answers before they are allowed to be shown.
- No MediatR, AutoMapper, generic repositories or new projects. A new NuGet package needs an ADR.

## Safety

- Render user Markdown only with the `<Markdown>` component; never `MarkupString` elsewhere.
- Dates are UTC and come from `TimeProvider`, never `DateTime.UtcNow`/`DateTime.Now`.
- Never log e-mails, tokens, solution text or AI prompts. No secrets in the repository.

## Process

- Product behavior comes from `docs/MVP_SPEC.md`; do not add P1 features. Record every implicit decision in `docs/DECISIONS.md`.
- Schema changes and `Contracts/` changes go in a separate PR labelled `schema-change`.
- UI text is in Polish; code, comments and commit messages are in English.
- End every final answer with a `## Next step` section: one concrete action for the human in the MentorPath workflow (see `AGENTS.md`).
- A task is done only when `dotnet build MentorPath.slnx` has 0 warnings and the relevant tests pass.
