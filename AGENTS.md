# MentorPath — instructions for AI agents

MentorPath is a mentoring platform for backend architecture (.NET): Scenario → Decision → Rationale → Feedback → Reflection → Progress. It is a multi-tenant modular monolith (.NET 10, Blazor Web App Interactive Server, PostgreSQL + EF Core, ASP.NET Core Identity, Azure Blob Storage). The product source of truth is `docs/MVP_SPEC.md`.

Universal invariants live in `.github/copilot-instructions.md`; path-specific rules live in `.github/instructions/`. Do not duplicate them here.

## Commands

```bash
docker compose up -d                                   # PostgreSQL, Azurite (Azure Blob), Mailpit
dotnet build MentorPath.slnx                           # must finish with 0 warnings (warnings are errors)
dotnet test tests/MentorPath.UnitTests
dotnet test tests/MentorPath.IntegrationTests          # needs Docker (Testcontainers)
dotnet test tests/MentorPath.IntegrationTests --filter "FullyQualifiedName~<Module>"   # fast loop while working
dotnet run --project src/MentorPath.Web -- seed-demo   # migrations + demo data
dotnet run --project src/MentorPath.Web
```

Integration tests use real PostgreSQL via Testcontainers. Never replace the database with mocks or an in-memory provider.

## Repository map

| Path | Contents |
|---|---|
| `src/MentorPath.Data` | `AppDbContext`, entities, EF configurations, migrations |
| `src/MentorPath.Application/<Module>` | Services and `Contracts/` (interfaces, DTOs). Modules: Workspaces, Mentorships, Invitations, Catalog, AiAuthoring, Roadmaps, Submissions, Reviews, Progress, Privacy, Common |
| `src/MentorPath.Web` | Blazor pages and components, endpoints, composition root, CLI commands |
| `tests/MentorPath.UnitTests` | Domain rules, validators, progress calculation |
| `tests/MentorPath.IntegrationTests` | Per-module tests, `Isolation/` (convention + isolation tests), `Release/` (end-to-end scenario) |
| `docs/` | See "Source of truth" below |

## Source of truth (in this order)

1. `docs/MVP_SPEC.md` — product scope.
2. `docs/DECISIONS.md` — decisions D-xx that refine the spec.
3. `docs/DOMAIN_RULES.md` — access rules, constraints, state machine, concurrency patterns, write ownership.
4. `docs/ARCHITECTURE.md` — structure and technical rules.
5. The code. Entities are in `src/MentorPath.Data/Entities`, DTOs and service interfaces in `src/MentorPath.Application/*/Contracts`. The code is authoritative for shapes; the docs are for rules and intent.
6. UI only: `PRODUCT.md` (users and tone) and `DESIGN.md` (tokens, layout templates, craft floor). Rules for agents are in `.github/instructions/razor-ui.instructions.md`.

If the docs do not answer a question, do not guess: add a `Proposed` entry to `docs/DECISIONS.md` and flag it in the PR. `docs/archive/` is history only; never use it as a requirement.

## Workflow

1. **Plan.** Work starts from a GitHub issue created from the `backlog-item` template (use the `mentorpath-planner` agent). It has acceptance criteria and an isolation matrix. If the issue lacks them, ask before coding.
2. **Implement** one issue per branch and per pull request. Use the `implement-backlog-item` skill. For a new service method follow `add-service-method`; for schema changes follow `add-migration`.
3. **Schema and `Contracts/` changes** go in their own PR labelled `schema-change`, merged before the feature PR. CODEOWNERS enforces human review.
4. **Verify** before opening the PR: build with 0 warnings, the module's tests, `ConventionTests`/`ArchitectureTests`. Generated code is not correct until these pass.
5. **Open a draft PR** using the template. Report what was done, tests run, risks and open questions. Do not hide problems.
6. **Review.** CI must pass. A human reviews; for changes touching authorization or data access, also run the `mentorpath-security-reviewer` agent.
7. **Board.** Work is tracked on the GitHub Project "MentorPath Backlog" (`Backlog → Planned → In progress → In review → Done`). Move cards with `scripts/board.sh <issue> <status>` (best effort; if it cannot, tell the human which card to move).

### Session flow for GitHub Copilot

Use the repo's issue-driven rhythm in the app:

- Start a session for a backlog item such as `pickup #<n>` or an equivalent issue-focused session name.
- In the first pass, keep the session in planning mode and confirm the issue, acceptance criteria, and isolation matrix.
- Once the plan is clear, switch to autopilot for implementation.
- At the end, ask whether the human wants to `ship` the change, then open the draft PR using the repo template.
- Start a separate review session afterward with `mentorpath-pr-reviewer` on a different model; if review comments remain, the implementer should `/address-review` them, otherwise end with a retro.

Repo-defined slash commands like `/pickup`, `/ship`, and `/pr-review` are not native to this GitHub Copilot project. The supported equivalent is this agent + model + issue/PR workflow, defined by the files in `.github/`.

**Always end your final answer with a `## Next step` section**: exactly one concrete action for the human with the literal prompt or link to use, chosen from the workflow above (answer open questions, merge the schema PR, run the security reviewer, review and merge, or what is blocking you). Never end without it.

## Definition of Done

- Acceptance criteria of the issue are met and each is covered by a test.
- Every public service method with a resource id has an integration test where a foreign mentor, a foreign mentee and an unknown user get `NotFound`.
- Server-side validation; loading, empty and error states in the UI.
- `dotnet build` has 0 warnings; unit and integration tests are green.
- New decisions are appended to `docs/DECISIONS.md`.
- No secrets, e-mails, tokens, solution text or prompts in logs or in the repo.

## Boundaries

- Scope is the MVP (P0) plus items explicitly in `docs/BACKLOG_NEXT.md`. Do not add anything else.
- Never edit an existing migration. Never use `git push --force` or rewrite shared history.
- A new NuGet package, a new project, or a change to `Directory.*.props`, `global.json` or workflows needs an ADR in `docs/DECISIONS.md` and CODEOWNERS review.
- Do not commit or push unless the user asked for it.
