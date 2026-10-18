# MentorPath

Platforma do indywidualnego mentoringu architektury backendowej (.NET): **Scenariusz → Decyzja → Uzasadnienie → Feedback → Refleksja → Postęp**.

Modularny monolit: .NET 10, ASP.NET Core, Blazor Web App (Interactive Server), PostgreSQL + EF Core, ASP.NET Core Identity, prywatny storage S3, xUnit + Testcontainers.

| Dokument | Zawartość |
|---|---|
| [docs/MVP_SPEC.md](docs/MVP_SPEC.md) | Specyfikacja produktu (źródło prawdy) |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Struktura solution, zależności, autoryzacja, testy |
| [docs/AGENT_CONTRACTS.md](docs/AGENT_CONTRACTS.md) | Encje, DTO, interfejsy serwisów, reguły współbieżności |
| [docs/DECISIONS.md](docs/DECISIONS.md) | Decyzje produktowe i techniczne |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | Plan, milestones, status realizacji MVP |
| [docs/BACKLOG_NEXT.md](docs/BACKLOG_NEXT.md) | Backlog rozwoju po MVP (fazy 0–5) na podstawie researchu |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Konfiguracja, wdrożenie, backup, operacje |
| [docs/SECURITY_REVIEW.md](docs/SECURITY_REVIEW.md) | Przegląd bezpieczeństwa i ryzyka |
| [AGENTS.md](AGENTS.md) | Zasady pracy agentów i własność plików |

## Szybki start (lokalnie)

Wymagania: .NET SDK 10.0.1xx+, Docker.

```bash
docker compose up -d                                         # PostgreSQL, S3 (SeaweedFS), Mailpit
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

## Komendy administracyjne

```bash
dotnet run --project src/MentorPath.Web -- migrate
dotnet run --project src/MentorPath.Web -- create-mentor --email anna@firma.pl --name "Anna Nowak" --workspace "Mentoring Anny"
dotnet run --project src/MentorPath.Web -- seed-demo        # tylko Development (lub --force)
```
`create-mentor` wypisuje jednorazowy link do ustawienia hasła. Publicznej rejestracji mentorów nie ma.

## Testy

```bash
dotnet test tests/MentorPath.UnitTests          # reguły domenowe, walidatory, XSS, postęp
dotnet test tests/MentorPath.IntegrationTests   # PostgreSQL (Testcontainers), HTTP, izolacja, scenariusz release
```

Smoke test przeglądarkowy całego przepływu (wymaga uruchomionej aplikacji z `seed-demo`):

```bash
cd tests/e2e && npm install && npm run install-browser
BASE_URL=http://localhost:5237 npm run smoke
```

## Struktura

```
src/MentorPath.Data          AppDbContext, encje, migracje (właściciel: A1)
src/MentorPath.Application   moduły: Workspaces, Mentorships, Invitations, Catalog, AiAuthoring,
                             Roadmaps, Submissions, Reviews, Progress + Common (autoryzacja, storage, markdown)
src/MentorPath.Web           Blazor UI, endpointy upload/download, CLI
tests/                       UnitTests, IntegrationTests, e2e (Playwright)
```
