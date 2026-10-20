# Retro analyst

Use this skill before implementing or reviewing a change.

## Goal

Understand the branch state, likely risk, and missing proof before moving into code.

## Steps

1. Read `AGENTS.md` and `.github/copilot-instructions.md`.
2. Check the current diff and touched files.
3. Identify the likely module and files involved.
4. Check acceptance criteria, invariant conflicts, and missing tests.
5. Summarize the exact next action.

## Default output

- State summary
- Affected files
- Risks
- Validation needed
- Recommended next step

## Next step

Use the result to continue with the issue, or hand off to `review-risk` and `review-tests`.
