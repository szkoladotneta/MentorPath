# PR review

Use this skill in a separate session after the implementation is ready to ship.

## Goal

Review the PR with a different model and decide whether the implementation is ready, needs fixes, or should end with a retro.

## Rules

- Start from the PR diff and the acceptance criteria.
- Focus on authorization, tenant isolation, data leakage, and test coverage.
- Post only substantive review comments.
- If the findings are actionable, tell the implementer to `/address-review`.
- If the review is clean, suggest a workflow retro to improve the process.

## Output

Return:
- summary of the review
- key findings
- whether the PR is ready
- required follow-up action
