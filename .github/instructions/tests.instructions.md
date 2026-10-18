---
applyTo: "tests/**"
---

# Tests

- Integration tests derive from `IntegrationTestBase` and use `Data` (`TestDataBuilder`) and `App.Get<T>()`. They run on real PostgreSQL via Testcontainers; do not mock the database.
- Assertions use the helpers in `Infrastructure/TestApp.cs`: `ShouldSucceed()`, `ShouldFail(ErrorKind, code)`, `ShouldBeNotFound()`.
- Isolation test shape: Mentor A owns the resource; Mentor B, a mentee of another mentorship and an unknown user all call the method and get `NotFound`. Put them in the module's folder or in `Isolation/`.
- Business-rule tests assert the exact `ErrorKind` and error code, not only that the call failed.
- Never weaken an assertion, delete a test or add to `IsolationCoverageDebt` in `ConventionTests` to get green. Fix the cause or report it.
- Tests are deterministic: use `TimeProvider`, no `Task.Delay`, no shared mutable state, unique data per test.
- Keep names descriptive: `Subject_expected_behavior_when_condition`.
