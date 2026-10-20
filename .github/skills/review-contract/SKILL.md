# Review contract

Use this skill when a change touches DTOs, service contracts, entities, or schema shape.

## Goal

Validate that the change preserves MentorPath boundaries and does not leak tenant or mentor private data.

## Checks

- `actorUserId` is first and access checks happen before data access.
- `WorkspaceId` is derived server-side.
- `Contracts/` and DTO changes are compatible with the module rules.
- No mentor-private data appears in mentee-facing DTOs.
- A schema or `Contracts/` change has the correct PR split.

## Output

Provide a short contract review and the follow-up required before continue/merge.
