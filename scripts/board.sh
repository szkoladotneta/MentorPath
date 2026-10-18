#!/usr/bin/env bash
# Moves an issue's card on the "MentorPath Backlog" board (https://github.com/users/szkoladotneta/projects/6).
# Usage: scripts/board.sh <issue-number> <Backlog|Planned|"In progress"|"In review"|Done>
# Best effort: needs a gh login with the `project` scope. On failure it prints what to do by hand and exits 0.
set -uo pipefail

OWNER="szkoladotneta"
PROJECT=6
ISSUE="${1:-}"
STATUS="${2:-}"

case "$STATUS" in
  Backlog | Planned | "In progress" | "In review" | Done) ;;
  *) echo "Usage: $0 <issue-number> <Backlog|Planned|In progress|In review|Done>" >&2; exit 2 ;;
esac
[[ "$ISSUE" =~ ^[0-9]+$ ]] || { echo "Issue number must be numeric" >&2; exit 2; }

manual() { echo "board.sh: could not update the board ($1). Move card #$ISSUE to '$STATUS' by hand: https://github.com/users/$OWNER/projects/$PROJECT"; exit 0; }

PROJECT_ID=$(gh project view "$PROJECT" --owner "$OWNER" --format json --jq .id 2>/dev/null) || manual "no access to the project; run: gh auth refresh -s project"
ITEM_ID=$(gh project item-list "$PROJECT" --owner "$OWNER" --limit 500 --format json --jq ".items[] | select(.content.number == $ISSUE) | .id" 2>/dev/null) || manual "item lookup failed"
[ -n "$ITEM_ID" ] || manual "issue #$ISSUE is not on the board"
FIELD=$(gh project field-list "$PROJECT" --owner "$OWNER" --format json --jq '.fields[] | select(.name == "Status")' 2>/dev/null) || manual "field lookup failed"
FIELD_ID=$(jq -r .id <<<"$FIELD")
OPTION_ID=$(jq -r --arg s "$STATUS" '.options[] | select(.name == $s) | .id' <<<"$FIELD")
[ -n "$OPTION_ID" ] || manual "status option not found"

gh project item-edit --id "$ITEM_ID" --project-id "$PROJECT_ID" --field-id "$FIELD_ID" --single-select-option-id "$OPTION_ID" >/dev/null 2>&1 \
  && echo "board: #$ISSUE -> $STATUS" || manual "update failed"
