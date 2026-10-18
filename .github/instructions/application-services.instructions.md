---
applyTo: "src/MentorPath.Application/**"
---

# Application services

- Module layout: `src/MentorPath.Application/<Module>/` with `Contracts/` (interface + DTOs + requests) and the implementation next to it. Look at an existing module (for example `Roadmaps`) and copy its structure.
- Return `Result` / `Result<T>` with `Errors.NotFound(Module)`, `Errors.Validation(code, message)` or `Errors.Conflict(code, message)`. Do not throw for expected failures.
- First statement after argument checks: resolve access via `IWorkspaceAccessService` for the actor and the resource. Only then query or write. On failure return `Errors.NotFound(Module)` for both "does not exist" and "not yours".
- Scope every query by ownership (workspace / mentorship); do not load by id alone and check afterwards.
- Reads: `AsNoTracking`. Writes: only tables owned by this module (see `docs/DOMAIN_RULES.md`).
- Idempotent writes use `ClientRequestId`; races are handled with the patterns in `docs/DOMAIN_RULES.md` (unique index + catch, or concurrency token).
- Take time from `TimeProvider`. Validation messages shown to users are in Polish; error codes are stable English ids such as `submission.already_completed`.
- Register new services in the module's `<Module>Module.cs` extension (for example `Roadmaps/RoadmapsModule.cs`).
- Every new public method needs: a unit test for rules, an integration test for the happy path, and an isolation test asserting `NotFound` for a foreign mentor, a foreign mentee and an unknown user.
