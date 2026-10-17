# AGENTS.md — zasady pracy agentów MentorPath

Obowiązuje każdego agenta (A1–A5) i Tech Leada (A0). Krótko: **trzymaj się swoich plików, nie zgaduj kontraktów, udowadniaj izolację testem.**

## 1. Źródła i ich kolejność

1. `docs/MVP_SPEC.md` — zakres produktu (źródło prawdy).
2. `docs/DECISIONS.md` — decyzje (Adopted/zatwierdzone Proposed) doprecyzowujące spec.
3. `docs/AGENT_CONTRACTS.md` — encje, DTO, interfejsy, reguły współbieżności.
4. `docs/ARCHITECTURE.md` — struktura, zależności, zasady techniczne.
5. `docs/IMPLEMENTATION_PLAN.md` — kolejność, bramki, zakres plików.
6. `.github/agents/*.agent.md` — rola agenta. **Jeśli nazwy encji/interfejsów w pliku agenta różnią się od AGENT_CONTRACTS (np. `Attempt`, `WorkspaceRelation`, `InvitationToken`, `MentorProfile`, `Rejected`), obowiązuje AGENT_CONTRACTS.**

Brak odpowiedzi w dokumentach → nie zgaduj: dopisz propozycję do `DECISIONS.md` (status Proposed) i zgłoś A0.

## 2. Twarde zasady

- Zakres = P0 z `BACKLOG.md`. Żadnych funkcji P1.
- Każda publiczna metoda serwisu przyjmuje `actorUserId` i sprawdza dostęp przez `IWorkspaceAccessService` **przed** odczytem/zapisem. Rola Identity to nie uprawnienie. ID w URL to nie uprawnienie.
- `WorkspaceId` nigdy nie pochodzi z requestu/UI (D-08). Brak dostępu = `NotFound`.
- Komponenty Razor nie używają `AppDbContext`/`IDbContextFactory`; tylko serwisy i DTO.
- Serwisy: `IDbContextFactory<AppDbContext>`, kontekst per metoda, `AsNoTracking` dla odczytów.
- Zapis tylko do tabel własnego modułu (AGENT_CONTRACTS §4).
- DTO dla mentee nigdy nie zawierają danych prywatnych mentora ani poprawnych odpowiedzi.
- Markdown renderuj wyłącznie komponentem `<Markdown>`.
- Daty UTC, czas z `TimeProvider`. Kod po angielsku, UI po polsku.
- Brak MediatR, AutoMapper, generic repository, global query filters, nowych projektów. Nowy pakiet NuGet = CR.
- Brak sekretów w repo; brak e-maili, tokenów, treści rozwiązań i promptów w logach.

## 3. Własność plików

| Obszar | Właściciel |
|---|---|
| `src/MentorPath.Data/**` (AppDbContext, encje, migracje), `Directory.*.props`, `*.csproj`, `global.json`, `Program.cs`, `Web/Composition/**`, `Web/Cli/**` (oprócz `DemoSeed.cs`), `Components/{App,Routes,_Imports}.razor`, `Components/Layout/**`, `Components/Shared/**`, `Components/Account/**`, `Application/Common/**`, moduły `Workspaces`, `Mentorships`, `Invitations` i ich strony, `tests/**/Infrastructure/**` | **A1** |
| `Application/{Catalog,AiAuthoring}/**`, `Pages/Mentor/{Catalog,Exercises}/**` | **A2** |
| `Application/{Roadmaps,Submissions}/**`, `Pages/Mentor/Roadmaps/**`, `Pages/Mentee/{Home,Roadmap,Assignments}/**`, `Web/Endpoints/{Attachment,PracticalSubmission}Endpoints.cs` | **A3** |
| `Application/{Reviews,Progress}/**`, `Pages/Mentor/{Reviews,Dashboard}/**`, `Pages/Mentee/Progress/**`, `Components/Modules/**` | **A4** |
| `tests/**/{Isolation,Release,Architecture}/**`, `Dockerfile`, `.github/workflows/**` (po B01), `Web/Cli/DemoSeed.cs`, `docs/{DEPLOYMENT,SECURITY_REVIEW,SMOKE_CHECKLIST}.md` | **A5** |
| `AGENTS.md`, `docs/{ARCHITECTURE,AGENT_CONTRACTS,IMPLEMENTATION_PLAN}.md` | **A0** |
| `docs/DECISIONS.md` | wszyscy dopisują (append-only); status zmienia A0 |

Testy modułu: `tests/MentorPath.{UnitTests,IntegrationTests}/<Module>/**` + `TestDataBuilder.<Module>.cs` — właściciel modułu.
Pliki `Contracts/` modułu: właściciel modułu, ale **każda** zmiana wymaga CR.
Potrzebujesz zmiany w cudzym pliku → CR lub zgłoszenie do właściciela. Nigdy nie edytuj sam.

## 4. Git i workflow

- Gałąź per zadanie: `a<N>/b<NN>-<slug>` (np. `a3/b10-quiz`), najlepiej w osobnym worktree: `git worktree add ../MentorPath-a3 -b a3/b10-quiz`.
- Małe PR do `main`, rebase przed merge, CI zielone, review A0.
- Commity: `B10: <opis>`; zmiany kontraktów/schematu: `CR-<nn>: <opis>`.
- Nie mergujemy niezbudowanego/nieprzetestowanego kodu. Wygenerowany kod nie jest poprawny, dopóki build i testy nie przejdą.

## 5. Change Request (kontrakt, schemat, pakiet, cudzy plik)

Dopisz do `docs/DECISIONS.md` → sekcja *Change Requests*:
```
## CR-<nn> — <tytuł>
Zgłasza: A<n> · Dotyczy: <pliki/kontrakty> · Wpływ na: A<x>, A<y>
Problem: ...
Propozycja: ...
Status: Requested | Approved (A0) | Done (<commit>)
```
Schemat/migracja/pakiet → po akceptacji implementuje A1 na gałęzi `a1/cr-<nn>-<slug>`, merge pierwszy, pozostali rebase.

## 6. Definition of Done (każde zadanie)

- Spełnia kryteria z `BACKLOG.md` i `IMPLEMENTATION_PLAN.md` §5.
- Walidacja serwerowa + stany loading/empty/error w UI.
- Test izolacji dla każdej metody z ID zasobu (Mentor B / obca mentee → `NotFound`).
- Testy reguł biznesowych i współbieżności, gdzie dotyczy.
- `dotnet build` (0 ostrzeżeń) i `dotnet test` zielone.
- Nowe decyzje zapisane w `DECISIONS.md`.
- Raport w PR: zrobione, testy, ryzyka, otwarte pytania. Nie ukrywaj problemów.

## 7. Komendy

```bash
docker compose up -d                               # postgres, minio, mailpit
dotnet build
dotnet test tests/MentorPath.UnitTests
dotnet test tests/MentorPath.IntegrationTests      # wymaga Dockera (Testcontainers)
dotnet run --project src/MentorPath.Web -- migrate
dotnet run --project src/MentorPath.Web -- create-mentor --email a@x.pl --name "Mentor A" --workspace "Workspace A"
dotnet run --project src/MentorPath.Web -- seed-dev
dotnet run --project src/MentorPath.Web
```
