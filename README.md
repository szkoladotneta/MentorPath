# MentorPath

Platforma do indywidualnego mentoringu architektury backendowej (.NET): **Scenariusz → Decyzja → Uzasadnienie → Feedback → Refleksja → Postęp**.

Modularny monolit: .NET 10, ASP.NET Core, Blazor Web App (Interactive Server), PostgreSQL + EF Core, ASP.NET Core Identity, prywatny Azure Blob Storage, xUnit + Testcontainers.

| Dokument | Zawartość |
|---|---|
| [docs/MVP_SPEC.md](docs/MVP_SPEC.md) | Specyfikacja produktu (źródło prawdy) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Struktura solution, zależności, autoryzacja, testy |
| [docs/DOMAIN_RULES.md](docs/DOMAIN_RULES.md) | Reguły domenowe: autoryzacja, ograniczenia bazy, stany, współbieżność, własność zapisu |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Decyzje produktowe i techniczne |
| [docs/BACKLOG_NEXT.md](docs/BACKLOG_NEXT.md) | Backlog rozwoju po MVP (fazy 0–5) na podstawie researchu |
| [docs/WORKFLOW.md](docs/WORKFLOW.md) | Jak pracować z agentami Copilota: od pozycji backlogu do merge |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Konfiguracja, wdrożenie, backup, operacje |
| [docs/AZURE_DEPLOYMENT.md](docs/AZURE_DEPLOYMENT.md) | Azure krok po kroku: zasoby, sekrety, obraz, migracje, pierwszy mentor |
| [docs/SECURITY_REVIEW.md](docs/SECURITY_REVIEW.md) | Przegląd bezpieczeństwa i ryzyka |
| [AGENTS.md](AGENTS.md) | Instrukcje dla agentów AI: komendy, mapa repo, workflow, DoD |
| [docs/archive/](docs/archive) | Dokumenty historyczne (plan MVP, backlog v1, research) — nie są wymaganiami |

## Szybki start (lokalnie)

Wymagania: .NET SDK 10.0.1xx+, Docker.

```bash
docker compose up -d                                         # PostgreSQL, Azurite (Azure Blob), Mailpit
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/MentorPath.Web -- seed-demo         # migracje + dane demo
dotnet run --project src/MentorPath.Web --launch-profile https
```

Aplikacja: https://localhost:7083 (certyfikat dev: `dotnet dev-certs https --trust`) · Mailpit (e-maile resetu hasła): http://localhost:8025

Konta demo (hasło `Demo-password-2026`):

| Konto | Rola |
|---|---|
| `mentor.a@mentorpath.local` | Mentor A — katalog, 3 tematy, roadmapa dla Oli |
| `mentor.b@mentorpath.local` | Mentor B — osobny workspace |
| `ola@mentorpath.local` | Mentee w obu workspace'ach |
| `piotr@mentorpath.local` | Mentee A bez roadmapy |

Pełny stack w kontenerach (obraz aplikacji + migracje):

```bash
docker compose --profile app up -d --build                  # http://localhost:8080
```

## Wdrożenie na Azure

Zacznij od [instrukcji Azure](docs/AZURE_DEPLOYMENT.md). Docelowy stos to App Service Linux (jeden kontener), Azure Database for PostgreSQL i prywatny Azure Blob Storage. Dostęp do plików przez managed identity, bez AWS/S3 i bez kluczy konta storage. Lokalnie Azurite zastępuje SeaweedFS; stare wolumeny S3 nie są automatycznie migrowane (procedura w instrukcji Azure).

## Komendy administracyjne

```bash
dotnet run --project src/MentorPath.Web -- migrate
dotnet run --project src/MentorPath.Web -- create-mentor --email anna@firma.pl --name "Anna Nowak" --workspace "Mentoring Anny"
dotnet run --project src/MentorPath.Web -- seed-demo        # tylko Development (lub --force)
```
`create-mentor` wypisuje jednorazowy link do ustawienia hasła. Publicznej rejestracji mentorów nie ma.

## Agenci AI (MCP)

MentorPath wystawia serwer [Model Context Protocol](https://modelcontextprotocol.io) (D-64): narzędzia do czytania mentees, katalogu, roadmap i postępu oraz do tworzenia tematów, szkiców ćwiczeń i roadmap (zawsze jako `Draft` — publikuje mentor).

```bash
dotnet build src/MentorPath.Web -c Release                       # binarium używane przez .mcp.json i .vscode/mcp.json
# Claude Code / VS Code (lokalnie, stdio, tylko Development):
dotnet src/MentorPath.Web/bin/Release/net10.0/MentorPath.Web.dll mcp-stdio --actor mentor.a@mentorpath.local
# Instancja z /mcp (Streamable HTTP, bearer JWT; wymaga Mcp__SigningKey):
dotnet run --project src/MentorPath.Web -- mcp-token --email mentor.a@mentorpath.local --days 30
claude mcp add --transport http mentorpath https://<host>/mcp --header "Authorization: Bearer <token>"
```

`scripts/demo-reset.sh` odtwarza stan demo (świeża baza, `seed-demo`, build Release).

## Testy

```bash
dotnet test tests/MentorPath.UnitTests          # reguły domenowe, walidatory, XSS, postęp
dotnet test tests/MentorPath.IntegrationTests   # PostgreSQL (Testcontainers), HTTP, izolacja, scenariusz release
```

Smoke test przeglądarkowy całego przepływu (`tests/e2e`) jest wymaganym jobem CI na pull requestach i pushach do `main` (D-55). Lokalnie wymaga uruchomionej aplikacji z `seed-demo`:

```bash
cd tests/e2e && npm ci && npm run install-browser
BASE_URL=http://localhost:5237 npm run smoke
```

## Struktura

```
src/MentorPath.Data          AppDbContext, encje, migracje
src/MentorPath.Application   moduły: Workspaces, Mentorships, Invitations, Catalog, AiAuthoring,
                             Roadmaps, Submissions, Reviews, Progress + Common (autoryzacja, storage, markdown)
src/MentorPath.Web           Blazor UI, endpointy upload/download, CLI
tests/                       UnitTests, IntegrationTests, e2e (Playwright)
```
