---
applyTo: "src/MentorPath.Data/**"
---

# Data layer and migrations

- Changes here are `schema-change` PRs: separate from feature code, labelled `schema-change`, reviewed by CODEOWNERS.
- Never edit or delete an existing migration. Add a new one: `dotnet ef migrations add <Name> --project src/MentorPath.Data --startup-project src/MentorPath.Web`.
- After adding a migration run `dotnet ef migrations has-pending-model-changes --project src/MentorPath.Data --startup-project src/MentorPath.Web`; it must report no pending changes. CI runs the same check.
- Entity configuration lives in `Configurations/`, not in attributes. Keep constraints, indexes and enum conversions there.
- No global query filters. Do not rely on the database for authorization.
- Prefer additive, backward-compatible migrations. Anything destructive (drop column/table, type change) needs an explicit decision in `docs/DECISIONS.md` and a data-preservation note in the PR.
- If the change affects DB constraints, update the constraint table in `docs/DOMAIN_RULES.md` in the same PR.
