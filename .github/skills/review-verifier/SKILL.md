# Review verifier

Use this skill before a PR is handed to a human reviewer.

## Goal

Run the required validation for the repository and report the verified status with evidence.

## Commands

```bash
dotnet build MentorPath.slnx --nologo
dotnet test tests/MentorPath.UnitTests --nologo
dotnet test tests/MentorPath.IntegrationTests --nologo
```

If the change is narrow, explain why a smaller slice is sufficient and still verify the relevant tests.

## Output

Report exact commands, pass/fail, warnings, and whether the PR is ready for human review.
