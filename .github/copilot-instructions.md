# MentorPath Copilot Instructions

## Project rules

- Treat `docs/MVP_SPEC.md` as the source of truth for product behavior.
- Treat `docs/BACKLOG.md` as the execution backlog and release gate.
- Do not add P1 features to the MVP.
- Freeze model contracts and DTOs before parallel code work begins.
- Only Agent A1 may edit `AppDbContext`, migrations, and central dependency injection.
- Do not approve parallel edits to the same files.
- Validate authorization logic for all data access around user-owned resources.
- Never rely on a raw resource ID in the URL as a permission check.
- Keep cross-workspace data isolated and require tests proving it.
- Record any implicit product decision in `docs/DECISIONS.md`.

## Release constraint

The final MVP must support the release scenario described in `docs/BACKLOG.md` and ensure Mentor B cannot access Mentor A's data.
