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

## Copilot workflow

For GitHub Copilot, use the workflow in this order so it behaves like the working Claude-style operating model without importing repo-specific hooks:

1. `mentorpath-pickup` for a new issue/session: start in planning mode, check acceptance criteria and isolation coverage, and only then switch to autopilot.
2. `retro-analyst` before implementation to confirm the branch state, likely module, and risk.
3. `mentorpath-planner` for issue triage when a backlog item needs acceptance criteria and isolation coverage.
4. `review-contract` and `review-risk` when contracts, DTOs, authorization, or access logic are touched.
5. `review-tests` before opening or reviewing the PR.
6. `review-verifier` before handing the work to a human reviewer.
7. `mentorpath-pr-reviewer` in a separate session/model after the change is ready to ship; it decides whether the implementer should `/address-review` or end with a retro.

This keeps the repo's MentorPath rules as the source of truth while adding the same review discipline as the stronger Claude workflow.

## Session rhythm

The supported GitHub Copilot rhythm in this repo is:

- Start a new session for the issue: `pickup #<n>` or the equivalent issue-focused session name.
- First pass: planning mode only. Read the issue, the relevant rules, and the likely module.
- Second pass: autopilot implementation after the plan is confirmed.
- End of implementation: ask whether the human wants to `ship` the change, then open the draft PR with the repository template.
- Separate review session: run `mentorpath-pr-reviewer` on a different model, post review comments, and tell the implementer to `/address-review` or end the workflow with a retro.

GitHub Copilot projects do not support arbitrary repo-defined slash commands like `/pickup`, `/ship`, or `/pr-review` in the same way as some other tools. The closest supported workflow is a model + agent + issue/PR discipline defined by these repo instructions and the agent files above.

## Process

- Product behavior comes from `docs/MVP_SPEC.md`; do not add P1 features. Record every implicit decision in `docs/DECISIONS.md`.
- Schema changes and `Contracts/` changes go in a separate PR labelled `schema-change`.
- UI text is in Polish; code, comments and commit messages are in English.
- End every final answer with a `## Next step` section: one concrete action for the human in the MentorPath workflow (see `AGENTS.md`).
- A task is done only when `dotnet build MentorPath.slnx` has 0 warnings and the relevant tests pass.
