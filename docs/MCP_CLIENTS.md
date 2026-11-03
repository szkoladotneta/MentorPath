# MentorPath MCP: rehearsal setup and client connections

As of 2 November 2026. Companion to D-64 in `DECISIONS.md`; the server itself is described in `ARCHITECTURE.md` §2, §5 and §9, the deployment settings in `DEPLOYMENT.md`.

## Before you start

Everything the demo needs is merged on `main` (PRs #89 and #90) and the pilot's `/mcp` endpoint is switched on; what remains is per-machine client setup. The overview drawing at the end of this section shows how the pieces connect.

- One tool set, two transports. 21 tools, 2 resources and 2 prompts are served either over **stdio** (`mcp-stdio --actor <mentor e-mail>`, a child process of the client on the local demo database, Development only) or over **HTTP** (`https://mentorpath-kd261022.azurewebsites.net/mcp`, a bearer JWT per mentor minted by `mcp-token`, 30 days by default, 90 at most).
- What agents may do (D-64): read mentees, catalog, roadmaps and progress; create topics, exercise drafts and Draft roadmaps. They never publish, never change a Published roadmap, and never see a mentee's e-mail or the mentor's private notes.
- Pilot state: `Mcp__SigningKey` is set on the App Service. An anonymous `POST /mcp` answers 401; a 404 would mean the key is missing and the endpoint is not mapped.
- Local demo accounts (from `seed-demo`): mentors `mentor.a@mentorpath.local` and `mentor.b@mentorpath.local` (password `Demo-password-2026`), mentees Ola (in both workspaces) and Piotr (workspace A, no roadmap yet).
- This guide assumes macOS with the .NET 10 SDK, Docker Desktop, `az` signed in as the pilot operator, `gh`, and Node.js for the Claude Desktop bridge.

```mermaid
flowchart LR
    subgraph Clients
        CC[Claude Code CLI / app]
        CD[Claude Desktop]
        VS[VS Code with Copilot]
        CLI[Copilot CLI]
        CA[Copilot coding agent]
    end
    STDIO["stdio · local demo<br/>mcp-stdio --actor mentor<br/>child process, Development only"]
    HTTP["HTTP · pilot on Azure<br/>POST /mcp with bearer JWT<br/>mcp-token, 60 calls/min"]
    MCP["MentorPath.Web / Mcp<br/>21 tools, 2 resources, 2 prompts<br/>actor from the token or --actor"]
    SVC["Contracts/ services<br/>authorization, workspace scope"]
    PG[(PostgreSQL)]
    Clients --> STDIO
    Clients --> HTTP
    STDIO --> MCP
    HTTP --> MCP
    MCP --> SVC --> PG
```

A client picks one transport; both hand the actor to the same tool layer, and every tool calls the Contracts/ services that enforce authorization and workspace isolation before anything reaches PostgreSQL.

## Consented review workflow (N56)

New review tools are discoverable but **disabled by default**, independently of the signing key.
Before real-data use, the operator must document permitted clients/providers and D-40 processing
agreements, set a clear `Mcp:Reviews:ProviderNotice`, and explicitly set `Mcp:Reviews:Enabled=true`
in the HTTP host or local stdio environment. Environment names are `Mcp__Reviews__ProviderNotice`
and `Mcp__Reviews__Enabled`. Missing/invalid enabled disclosure configuration fails startup;
setting it is not legal approval.

The participating mentee separately grants consent on `/app/profile?mentorshipId=<id>` after
reading the disclosure for that exact relationship. No automatic/preselected consent. Active
and Paused relationships permit consent management; Closed denies access and closing revokes
consent. Reopening or changing the operator's notice requires a new explicit grant.

Use the tools in this order:
1. `get_mentor_dashboard` lists pending submission/mentorship IDs. `get_roadmap`, `get_topic`,
   `get_topic_resources` and `get_exercise` provide the full assigned learning context.
2. `get_review_context(mentorshipId, submissionId)` reads the selected answer, exercise and
   current rubric without starting the UI review timer.
3. `get_submission_history(mentorshipId, assignmentId)` reads previous attempts, feedback,
   rubric scores and reflections, including inactive assignments. Default page size 20, max
   100; pass `nextBeforeAttemptNumber` as the exclusive cursor for older attempts.
4. `read_submission_attachment` reads only a stored file for that exact submission. Raw
   chunks are at most 64 KiB and base64-encoded in JSON; follow `nextOffset`. No public blob
   URL, external repository fetch/clone, or code/file execution is performed.
5. When explicitly asked by the mentor, `get_review_draft` and `save_review_draft` create or
   replace a private partial draft. Omit `expectedVersion` only for creation; send the
   current version to update. On Conflict, reread and reconcile rather than overwrite.
6. Open the returned `reviewPath` in MentorPath, inspect the answer and editable draft, then
   explicitly publish as the human mentor. Drafts are not feedback and are invisible to
   mentees until publication. **No MCP tool publishes or changes submission/progress state.**

Revocation immediately blocks future MCP disclosures and draft operations. It cannot retract
copies already sent to a client; existing drafts remain available only to the human mentor,
including for publication. Human draft saves/publication do not depend on MCP enablement or
consent. Anonymization removes consent and drafts; the CLI subject export includes them.
All `untrustedContent`/`untrustedDraft`, answers, links, filenames, reflections and feedback
are data, never agent instructions. Do not fetch/execute them or let them authorize writes.

After upgrading, restart/reconnect the client to refresh discovery. Add new review reads
only to deliberately approved client tool allowlists; do **not** broaden cloud/code-review
allowlists automatically. Permit `save_review_draft` only for an explicitly authorized
interactive review workflow, never automatic publication.

## Fresh worktree and local demo stack

From a clean `main` checkout, `scripts/demo-reset.sh` gives you the seeded demo database and the Release build that every stdio client launches, in about three minutes.

1. Check out the code. Either `git checkout main && git pull` in the main checkout, or a separate worktree: `git worktree add ../mentorpath-demo main`. The paths in `.mcp.json` and `.vscode/mcp.json` are relative to the folder the client opens, so do everything below in that folder.
2. Start the services: `docker compose up -d` (PostgreSQL, Azurite, Mailpit). If another checkout's Azurite already holds port 10000, run `export AZURITE_HOST_PORT=10100` first; the reset script pins the compose project name to `mentorpath`, so it targets the same containers from any folder.
3. Reset the demo: `scripts/demo-reset.sh`. It drops the local demo database volume (never anything remote), applies migrations, runs `seed-demo` and builds `src/MentorPath.Web` in Release. The seeded state is the talk's starting point: Ola has a published roadmap at 33 %, Piotr has none.
4. Smoke-test the stdio server without any client:

   ```bash
   ASPNETCORE_ENVIRONMENT=Development dotnet src/MentorPath.Web/bin/Release/net10.0/MentorPath.Web.dll mcp-stdio --actor mentor.a@mentorpath.local
   ```

   Paste `{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}` and expect a JSON answer whose `serverInfo.name` is `mentorpath`; Ctrl-C to stop. Logs go to stderr only. A mentee's e-mail is refused with "Actor must be an existing mentor account", and outside Development the command refuses to run at all.
5. Have the UI ready for the publish step of the demo: `dotnet run --project src/MentorPath.Web`, then sign in at `https://localhost:7083` as `mentor.a@mentorpath.local` / `Demo-password-2026`.
6. Before every rehearsal, run `scripts/demo-reset.sh` again: the roadmap and quiz draft the agent creates for Piotr persist otherwise, and the demo expects Piotr to start without one.
7. After pulling new commits, `dotnet build MentorPath.slnx` must end with 0 warnings; `dotnet test tests/MentorPath.IntegrationTests --filter "FullyQualifiedName~Mcp"` runs the MCP tests in about 10 seconds (Docker needed).

## Local HTTP mode (optional)

The local app can serve `/mcp` exactly like the pilot, which lets you rehearse the bearer-token path without touching Azure. Five commands, all in the checkout folder:

```bash
dotnet run --project src/MentorPath.Web -- mcp-key                              # prints one line: mpk1:... (32 random bytes, Base64)
dotnet user-secrets set "Mcp:SigningKey" "mpk1:..." --project src/MentorPath.Web   # paste that line; any other format is refused at start-up
dotnet run --project src/MentorPath.Web                                         # /mcp is mapped only when the key is set

# in a second terminal: a one-day token for mentor.a (stdout = the token only; the notice goes to stderr)
cd src/MentorPath.Web && export ASPNETCORE_ENVIRONMENT=Development
export MENTORPATH_MCP_TOKEN="$(dotnet bin/Release/net10.0/MentorPath.Web.dll mcp-token --email mentor.a@mentorpath.local --days 1)"; cd -
curl -s -X POST http://localhost:5237/mcp -H "Authorization: Bearer $MENTORPATH_MCP_TOKEN" \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'
```

- The answer is a JSON-RPC result with `serverInfo.name` = `mentorpath`. Without the header you get 401 with `WWW-Authenticate: Bearer`; without the key you get 404.
- The token's audience is the app's `App:PublicBaseUrl` (`https://localhost:7083` in Development), so mint and serve from the same configuration.
- `mcp-token` needs `ASPNETCORE_ENVIRONMENT=Development` when run from the DLL: in Production the host insists on the Data Protection certificate, which the CLI does not use. It refuses non-mentor accounts, `--days` outside 1 to 90, and keys not generated by `mcp-key`.
- Revocation is per account: resetting the mentor's password (or any security-stamp refresh), a lockout, or losing the Mentor role ends the token on the next request. Rotating the key ends every token.
- Rate limit: 60 requests per minute per mentor; the 61st answers 429.

## Pilot on Azure: key and token

The pilot already has its signing key, so the only recurring step is minting a token for your mentor account with the operator CLI, which runs locally against the pilot database.

- Names: resource group `mentorpath-pilot-kd261022`, web app `mentorpath-kd261022`, PostgreSQL `mentorpath-pg-kd261022`, Key Vault `mp-kv-kd261022` (secret `db-connection`). The endpoint is `https://mentorpath-kd261022.azurewebsites.net/mcp`.
- The key (`Mcp__SigningKey`, an App Service setting) was generated with `mcp-key` and set on 30 Oct 2026. Changing it restarts the app for about a minute and revokes every token. The audience defaults to `App__PublicBaseUrl`, so a token works on this instance only.

Mint a token (paste the whole block in one shell; nothing prints a secret):

```bash
MENTOR_EMAIL=your-mentor-login@domain.pl
export ASPNETCORE_ENVIRONMENT=Development
export App__PublicBaseUrl=https://mentorpath-kd261022.azurewebsites.net
export ConnectionStrings__Default="$(az keyvault secret show --vault-name mp-kv-kd261022 --name db-connection --query value -o tsv)"
export Mcp__SigningKey="$(az webapp config appsettings list -g mentorpath-pilot-kd261022 -n mentorpath-kd261022 --query "[?name=='Mcp__SigningKey'].value | [0]" -o tsv)"
echo "conn_chars=${#ConnectionStrings__Default} key_chars=${#Mcp__SigningKey}"      # expect key_chars=49 and conn_chars > 0
MYIP="$(curl -s https://api.ipify.org)"
az postgres flexible-server firewall-rule create -g mentorpath-pilot-kd261022 -n mentorpath-pg-kd261022 -r "operator-$(whoami)" --start-ip-address "$MYIP" --end-ip-address "$MYIP" -o none
cd src/MentorPath.Web
export MENTORPATH_MCP_TOKEN="$(dotnet bin/Release/net10.0/MentorPath.Web.dll mcp-token --email "$MENTOR_EMAIL" --days 1)"
cd -
echo "token_chars=${#MENTORPATH_MCP_TOKEN}"                                              # a few hundred; 0 means the step above printed an error
```

- Use `--days 1` for a rehearsal and up to `--days 90` for daily work. The notice line names the user id and the audience, never the token.
- Remove the firewall rule when done: `az postgres flexible-server firewall-rule delete -g mentorpath-pilot-kd261022 -n mentorpath-pg-kd261022 -r "operator-$(whoami)" --yes`.
- Health checks before a demo: `curl -s -o /dev/null -w '%{http_code}\n' https://mentorpath-kd261022.azurewebsites.net/health/ready` must print 200, and an anonymous `POST /mcp` must answer 401, not 404.
- The full operator runbook (certificate, key ring, releases) is `AZURE_DEPLOYMENT.md`; the MCP settings are the `Mcp__SigningKey` and `Mcp__Audience` rows in `DEPLOYMENT.md`.

## Claude Code (CLI and the desktop app's Code tab)

The repository's `.mcp.json` already defines both servers, so Claude Code needs only the Release build in the opened folder and, for the pilot, the token in its environment.

| Server in `.mcp.json` | What it launches | Needs |
|---|---|---|
| `mentorpath` | `dotnet src/MentorPath.Web/bin/Release/net10.0/MentorPath.Web.dll mcp-stdio --actor mentor.a@mentorpath.local` with `ASPNETCORE_ENVIRONMENT=Development` | the Release build from `scripts/demo-reset.sh` in this folder, Docker up |
| `mentorpath-live` | HTTP to `https://mentorpath-kd261022.azurewebsites.net/mcp` with `Authorization: Bearer ${MENTORPATH_MCP_TOKEN}` | `MENTORPATH_MCP_TOKEN` set in the environment Claude Code starts in |

1. CLI: `export MENTORPATH_MCP_TOKEN=...` in the shell (from the mint block above), then `claude` in the checkout. On first use Claude Code asks you to approve the project-scoped servers; `claude mcp reset-project-choices` re-asks later.
2. Desktop app (Code tab): a GUI app does not inherit your shell variables. Either run `launchctl setenv MENTORPATH_MCP_TOKEN "$MENTORPATH_MCP_TOKEN"` and fully restart the app, or register the pilot once at user scope with the token baked into `~/.claude.json`, under a name that does not collide with the project one:

   ```bash
   claude mcp add --transport http --scope user mentorpath-azure https://mentorpath-kd261022.azurewebsites.net/mcp \
     --header "Authorization: Bearer $MENTORPATH_MCP_TOKEN"
   ```

   Re-run it after every new token (`claude mcp remove mentorpath-azure --scope user` first). The variable form never enters shell history.
3. Verify: `/mcp` inside Claude Code shows each server as connected with its tool count; `claude mcp list` shows the health per server, and `claude mcp get mentorpath-live` prints an `Issue:` line with the HTTP status when it fails. `/mcp reconnect all` retries every failed server after a fix.
4. Switching the actor for demo step 5: change `--actor` in `.mcp.json` to `mentor.b@mentorpath.local`, then `/mcp` and reconnect `mentorpath`. Do not commit that change.
5. If the first start of the stdio server times out (cold JIT), start with `MCP_TIMEOUT=10000 claude`.
6. Tool calls ask for permission per tool unless you allow them; the server's descriptions tell the agent which ids come from which list tool, so let it call `list_mentees` first.

## Claude Desktop

Claude Desktop runs the local demo as a stdio server from its config file and reaches the pilot as a custom connector with the bearer token as a request header; neither needs the repo open.

**Local demo (stdio).** Claude menu → Settings… → Developer → Edit Config opens `~/Library/Application Support/Claude/claude_desktop_config.json`. Paths must be absolute (no `~`, no variables): take `which dotnet` and the checkout's `pwd`.

```json
{
  "mcpServers": {
    "mentorpath": {
      "command": "/usr/local/share/dotnet/dotnet",
      "args": ["/ABSOLUTE/PATH/TO/MentorPath/src/MentorPath.Web/bin/Release/net10.0/MentorPath.Web.dll", "mcp-stdio", "--actor", "mentor.a@mentorpath.local"],
      "env": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

Quit and restart Claude Desktop completely, then open the "+" at the lower left of the chat box → Connectors → Manage connectors and check that `mentorpath` lists its tools. Docker and the Release build must exist before the app starts. Logs: `tail -n 20 -f ~/Library/Logs/Claude/mcp*.log`; the server's own stderr lands in `mcp-server-mentorpath.log`.

**Pilot (custom connector).** In the app or on claude.ai: Customize → Connectors → + Add → Add custom connector. Name it, paste `https://mentorpath-kd261022.azurewebsites.net/mcp`, Continue; set Authentication to "No sign in" and add a request header `Authorization` with the value `Bearer <token>`; Add. Enable it per chat with the "+" → Connectors toggle. The pilot is publicly reachable, which this path requires. A connector cannot be edited: for a new token, remove it and add it again.

**Alternative without the cloud path (bridge).** With Node 18 or newer, a second stdio entry runs `mcp-remote` as a local bridge; the token lives in `env`, and the header is written without a space after the colon so the client cannot mangle it:

```json
"mentorpath-live": {
  "command": "npx",
  "args": ["-y", "mcp-remote", "https://mentorpath-kd261022.azurewebsites.net/mcp", "--transport", "http-only", "--header", "Authorization:${AUTH_HEADER}"],
  "env": { "AUTH_HEADER": "Bearer <token>" }
}
```

Either pilot path stores the token outside the shell: the connector on Anthropic's side, the bridge in a file in your home folder, so prefer a one-day token for rehearsals and `rm -rf ~/.mcp-auth` if the bridge caches a stale state.

## GitHub Copilot

VS Code needs nothing beyond the committed `.vscode/mcp.json` and the token typed once into a password prompt; the Copilot CLI reads the same `.mcp.json` as Claude Code; the cloud coding agent takes a repository-level configuration with the token as a secret.

**VS Code (Copilot Chat, agent mode).** The file defines `mentorpath` (stdio, path built from `${workspaceFolder}`, Development environment) and `mentorpath-live` (HTTP, header from the input `mentorpath-token`, declared with `"password": true`, so VS Code prompts once and stores it as a secret).

1. Open the checkout folder and trust the workspace; run `scripts/demo-reset.sh` first so the Release build exists.
2. Open Copilot Chat, switch the mode picker to Agent.
3. Start a server: the Start link above its entry in `.vscode/mcp.json`, or Command Palette → MCP: List Servers → pick it → Start. `mentorpath-live` asks for the token at that point.
4. Pick tools in the Tools button of the chat input (Chat: Open Customizations → Tools lists them too). Every tool call shows a confirmation with the server name; expand a tool call to see its output.
5. Problems: MCP: List Servers → the server → Show Output. VS Code's newer guided flow (Chat: Open Customizations → MCP Servers → Add Server) labels the file-based configuration as deprecated but still reads it.
6. Actor switch for demo step 5: edit `--actor` in `.vscode/mcp.json`, then Restart the server from the same menu.

**Copilot CLI.** After `copilot` is started in the checkout and the folder is trusted, it loads `.mcp.json` from the repository root, so `mentorpath` works at once. If `mentorpath-live` reports 401, the CLI did not expand `${MENTORPATH_MCP_TOKEN}`; register the pilot explicitly, which writes `~/.copilot/mcp-config.json`:

```bash
copilot mcp add --transport http --header "Authorization: Bearer $MENTORPATH_MCP_TOKEN" mentorpath-azure https://mentorpath-kd261022.azurewebsites.net/mcp
```

Then `/mcp` shows the dashboard and `/mcp show mentorpath-azure` the tools. Check the file afterwards: one reported CLI version did not persist headers added through the interactive `/mcp add` form, so write the `headers` entry by hand if it is missing.

**Copilot coding agent (github.com).** Repository → Settings → Copilot → MCP servers → MCP configuration. Store the token as an Agents secret named `COPILOT_MCP_MENTORPATH_TOKEN` (only names with the `COPILOT_MCP_` prefix reach the configuration). OAuth servers are not supported there, which is fine: MentorPath uses bearer tokens. The `tools` list is mandatory and the agent uses them without asking, so allow the read tools only:

```json
{
  "mcpServers": {
    "mentorpath": {
      "type": "http",
      "url": "https://mentorpath-kd261022.azurewebsites.net/mcp",
      "headers": { "Authorization": "Bearer $COPILOT_MCP_MENTORPATH_TOKEN" },
      "tools": ["list_mentees", "get_mentee_profile", "get_mentor_dashboard", "list_topics", "get_topic", "list_exercises", "get_exercise", "get_roadmap", "get_roadmap_progress"]
    }
  }
}
```

Copilot code review only uses tools whose `readOnlyHint` is true; MentorPath marks all nine read tools that way. A token there carries one mentor's full rights for up to 90 days, so mint it for a dedicated mentor account rather than your own.

## Rehearsal checklist and the five prompts

Run the checks in this order; it is the order in which things fail on the day.

- [ ] `scripts/demo-reset.sh` ran today in the opened folder (Ola at 33 %, Piotr without a roadmap)
- [ ] UI running at `https://localhost:7083`, signed in as `mentor.a@mentorpath.local`
- [ ] The client shows `mentorpath` connected with 15 tools (`/mcp` in Claude Code, Manage connectors in Claude Desktop, MCP: List Servers in VS Code)
- [ ] Pilot part only: a token minted today, `mentorpath-live` connected, `/health/ready` answers 200
- [ ] Approve tool calls as they come; let the agent call `list_mentees` before anything that needs an id

| Step | Prompt | Tools the agent should use | Expected outcome |
|---|---|---|---|
| 1 | Which mentees do I have and where is each of them in their roadmap? | `list_mentees`, `get_mentor_dashboard`, `get_roadmap_progress` | Ola at 33 % on a published roadmap; Piotr has no roadmap |
| 2 | Look at Piotr's onboarding profile and my catalog. Propose a 3-topic roadmap for him and explain the order. | `get_mentee_profile`, the `catalog` resource or `list_topics` + `get_topic` | Says Piotr has not completed onboarding; proposes Modularny monolit → Spójność danych → Idempotencja API |
| 3 | Create that roadmap for Piotr with those topics in that order and assign the published exercises. Do not publish the roadmap. | `create_roadmap`, `add_roadmap_topic` ×3, `assign_exercise` ×3, `get_roadmap` | A Draft with 3 topics and 3 assignments; the agent points to `/mentor/mentees/{id}/roadmap` for publishing |
| 4 | Draft a quiz for the first topic and assign it to that topic. | `create_exercise_draft` (source AI), `assign_exercise` | `roadmap.exercise_not_published`; the agent explains only the mentor publishes, in the UI |
| 5 | (after switching `--actor` to `mentor.b`) Show Piotr's roadmap. | `get_roadmap` | `roadmap.not_found`: another mentor's data does not exist for this actor |
| bonus | Add the idempotency topic to Ola's roadmap. | `add_roadmap_topic` | `roadmap.published`: agents change Draft roadmaps only |

| Symptom | Cause | Fix |
|---|---|---|
| `mentorpath`: Connection closed | No Release build in the opened folder, Docker down, or the actor is not a mentor | `scripts/demo-reset.sh`; run the `mcp-stdio` command by hand to read its stderr; `MCP_TIMEOUT=10000` for a cold start |
| `POST /mcp` answers 404 | `Mcp__SigningKey` not set on that host | Set it (`mcp-key` format) and wait for the restart; 401 is the healthy anonymous answer |
| 401 `invalid_token` | Expired, tampered, minted for another instance (audience), or the key was rotated | Mint again against the same `App__PublicBaseUrl` and key |
| 401 right after a password reset, lockout or role change | By design: the token is bound to the account's security stamp and role | Mint a new token |
| 429 | 60 requests per minute per mentor | Wait a minute; the agent rarely needs more than ten calls per task |
| `roadmap.published` | The agent tried to change a Published roadmap | Expected; the mentor edits it in the UI |
| `roadmap.exercise_not_published` | A draft was assigned | Expected in step 4; publish the draft in the UI first |
| `*.not_found` | Another mentor's resource, a wrong id, or a mentee actor | Expected in step 5; otherwise re-read ids from the list tools |
| Claude Desktop shows no server | Relative paths, a stale app, or `dotnet` not on the app's PATH | Absolute paths in the config, full restart, `~/Library/Logs/Claude/mcp-server-mentorpath.log` |
| "must be a key generated by mcp-key" at start-up | A passphrase or an old-format key | Generate with `mcp-key` and set that value |

## Sources

- [Claude Code: Connect Claude Code to tools via MCP](https://code.claude.com/docs/en/mcp)
- [MCP quickstart: Connect to local MCP servers (Claude Desktop)](https://modelcontextprotocol.io/quickstart/user)
- [Claude Help Center: Getting started with custom connectors using remote MCP](https://support.claude.com/en/articles/11175166-getting-started-with-custom-connectors-using-remote-mcp)
- [VS Code: Add an MCP server](https://code.visualstudio.com/docs/copilot/customization/mcp-servers)
- [GitHub Docs: Adding MCP servers for GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers)
- [GitHub Docs: Extending Copilot coding agent with MCP](https://docs.github.com/en/copilot/how-tos/agents/copilot-coding-agent/extending-copilot-coding-agent-with-mcp)
- [mcp-remote README](https://github.com/geelen/mcp-remote)
- Repository: `DECISIONS.md` (D-64), `DEPLOYMENT.md`, `AZURE_DEPLOYMENT.md`, `.mcp.json`, `.vscode/mcp.json`, `scripts/demo-reset.sh`
