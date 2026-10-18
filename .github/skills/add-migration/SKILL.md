---
name: add-migration
description: Change the MentorPath database schema (entity, EF configuration, migration) safely. Use when an entity or its configuration changes or a table, column or index is added.
---

# Add a migration

Schema changes are always a separate PR labelled `schema-change`, merged before the code that uses them.

1. Edit the entity in `src/MentorPath.Data/Entities` and its configuration in `src/MentorPath.Data/Configurations`. Constraints, indexes and enum conversions belong in the configuration.
2. If the table is new or its owner changes, update the write-ownership matrix in `docs/DOMAIN_RULES.md` and in `WriteOwnership` in `tests/MentorPath.IntegrationTests/Isolation/ConventionTests.cs`.
3. Create the migration (never edit an existing one):
   ```bash
   dotnet ef migrations add <PascalCaseName> --project src/MentorPath.Data --startup-project src/MentorPath.Web
   ```
4. Review the generated SQL-producing code. Additive and backward-compatible is the default. Destructive operations need a decision in `docs/DECISIONS.md` and a data-preservation note in the PR.
5. Verify:
   ```bash
   dotnet ef migrations has-pending-model-changes --project src/MentorPath.Data --startup-project src/MentorPath.Web
   dotnet build MentorPath.slnx
   dotnet test tests/MentorPath.IntegrationTests     # applies all migrations on an empty database
   ```
6. Open the PR with the `schema-change` label. CODEOWNERS requires human review.

## Next step (always end with this)

End your answer with a `## Next step` section: "Open the PR with label `schema-change`, wait for CI (`has-pending-model-changes`), merge it yourself (CODEOWNERS), then say `Continue #<issue>` to start the feature PR."
