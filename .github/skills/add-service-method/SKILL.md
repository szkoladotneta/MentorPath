---
name: add-service-method
description: Add a public method to a MentorPath application service with correct authorization, tenant isolation and tests. Use whenever a service method that takes a resource id or reads or writes tenant data is added or changed.
---

# Add a service method

1. **Contract.** Add the method to the module's interface in `src/MentorPath.Application/<Module>/Contracts/`. First parameter `Guid actorUserId`, last `CancellationToken ct`, return `Task<Result>` or `Task<Result<T>>`. Adding to `Contracts/` is a `schema-change` PR if the issue did not already include it.
2. **Implementation.** In the module's service class:
   ```csharp
   await using var db = await dbFactory.CreateDbContextAsync(ct);
   var access = await accessService.<AppropriateCheck>(actorUserId, resourceId, ct);
   if (access is null) return Errors.NotFound(Module);   // same result for "missing" and "not yours"
   // query scoped by access.WorkspaceId / MentorshipId, AsNoTracking for reads
   ```
   Pick the access check that already exists in `IWorkspaceAccessService` (mentor of workspace, mentee of mentorship, etc.). Do not add a new one without an ADR. Look at a neighbouring method in the same service and mirror it exactly.
3. **Validate** inputs on the server and return `Errors.Validation(code, polishMessage)`; return `Errors.Conflict` for state violations.
4. **Writes.** Only tables owned by the module (`docs/DOMAIN_RULES.md`). Use `TimeProvider` for timestamps. For client-retried writes use `ClientRequestId`.
5. **Register** the service in the module's `<Module>Module.cs` if it is new.
6. **Tests**:
   - Unit test for pure rules.
   - Integration test for the happy path and each business rule (exact `ErrorKind` and code).
   - Isolation test: foreign mentor, foreign mentee and an unknown user all get `.ShouldBeNotFound()`.
7. Run `dotnet test tests/MentorPath.IntegrationTests --filter "FullyQualifiedName~ConventionTests"`. It must pass; it fails when the method lacks `actorUserId` first or a `NotFound` test.

## Next step (always end with this)

End your answer with a `## Next step` section: if the method takes a resource id, "Run `mentorpath-security-reviewer` on this branch before merging"; otherwise "Continue with the next step of the issue (UI, tests), then verify with build and tests".
