# Pickup

Use this skill when a human starts a new work session with a backlog item number, for example `#123` or `pickup #123`.

## Goal

Decide whether the issue is ready for implementation or needs planning first, then produce the exact next action.

## Procedure

1. Read `AGENTS.md`, `.github/copilot-instructions.md`, and the target issue.
2. Check acceptance criteria, module scope, and isolation matrix.
3. Determine if the issue needs a plan-first step or is ready for autopilot implementation.
4. Check whether a schema or `Contracts/` change requires a separate `schema-change` PR.
5. Validate that the author has the information required for implementation.

## Output

Return:
- issue readiness
- recommended mode: planning or autopilot
- module and likely files
- risks or missing data
- exact next action

## Session rule

The first pass is always planning mode. The second pass is the implementation pass.
