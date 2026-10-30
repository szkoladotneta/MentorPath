#!/usr/bin/env bash
# Resets the local demo stack to the exact state the talk demos start from (D-64):
# fresh PostgreSQL volume, migrations + seed-demo, and the Release build that .mcp.json / .vscode/mcp.json launch over stdio.
set -euo pipefail
cd "$(dirname "$0")/.."
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-mentorpath}"   # same project from any checkout or worktree

docker compose down -v
docker compose up -d
until docker compose exec -T postgres pg_isready -U mentorpath -d mentorpath >/dev/null 2>&1; do sleep 1; done

export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/MentorPath.Web -- seed-demo
dotnet build src/MentorPath.Web -c Release

echo
echo "Demo stack ready."
echo "  UI:        https://localhost:7083  (mentor.a@mentorpath.local / Demo-password-2026)"
echo "  MCP stdio: dotnet src/MentorPath.Web/bin/Release/net10.0/MentorPath.Web.dll mcp-stdio --actor mentor.a@mentorpath.local"
