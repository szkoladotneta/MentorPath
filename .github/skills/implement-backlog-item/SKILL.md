---
name: implement-backlog-item
description: Implement one MentorPath GitHub issue (backlog item N-xx) end to end - read the issue, implement, verify with build and tests, open a draft PR. Use when asked to implement, work on or pick up an issue or backlog item.
---

# Implement a backlog item

## 1. Understand

0. Move the card: `scripts/board.sh <issue-number> "In progress"` (best effort).
1. Read the issue completely: goal, acceptance criteria, isolation matrix, out of scope, open questions. If acceptance criteria or the isolation matrix are missing, or open questions block the work, stop and ask. Do not guess.
2. Read the relevant parts of `docs/MVP_SPEC.md`, `docs/DECISIONS.md` and `docs/DOMAIN_RULES.md`.
3. Read one existing, similar feature in the same module and follow its structure.

## 2. Decide the PR split

- If the issue needs an entity, migration or `Contracts/` change, that goes first in its own PR labelled `schema-change` (follow the `add-migration` skill). Stop after it and let a human merge it before the feature PR.
- Otherwise continue on one branch named `<issue-number>-<short-slug>`.

## 3. Implement

- Add service methods with the `add-service-method` skill.
- UI work follows `.github/instructions/razor-ui.instructions.md`.
- Write the tests together with the code: acceptance criteria first, then the isolation matrix row by row.

## 4. Verify (all must pass before opening the PR)

```bash
dotnet build MentorPath.slnx                                         # 0 warnings
dotnet test tests/MentorPath.UnitTests
dotnet test tests/MentorPath.IntegrationTests                        # needs Docker
```

If something fails, fix the cause. Do not weaken tests or add to `IsolationCoverageDebt`.

## 5. Open a draft PR

Use `.github/pull_request_template.md`. Link the issue with `Closes #<n>`. Fill in: what was done, tests run, risks, open questions, new decisions (D-xx). Append any new decision to `docs/DECISIONS.md`.
Then move the card: `scripts/board.sh <issue-number> "In review"` (best effort).

## 6. Next step (always end with this)

End your final answer with a `## Next step` section with exactly one recommendation:

- You opened a `schema-change` PR -> "Review and merge <PR url> yourself (CODEOWNERS). Then say `Continue #<n>` for the feature PR."
- You opened a feature PR that touches authorization or data access -> "Run `mentorpath-security-reviewer` on this branch."
- You opened any other feature PR -> "Wait for CI, then do the human review (acceptance criteria <-> tests, isolation matrix <-> `NotFound` tests, new decisions) and merge."
- You stopped on an open question or a missing acceptance criterion -> "Answer: <the exact question>. Then say `Continue #<n>`."
- Tests or CI failed and you could not fix them -> say what failed and what you need from the human.
