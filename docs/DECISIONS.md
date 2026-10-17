# MentorPath — Decision Log

Statusy: **Adopted** (obowiązuje) · **Proposed — APPROVAL REQUIRED** (blokuje start pracy, której dotyczy) · **Proposed** (domyślnie przyjęte, jeśli brak sprzeciwu przy akceptacji M0) · **Superseded**.

Każda decyzja: kontekst → decyzja → wpływ. Change Requesty (CR-xx) dopisujemy na końcu (format w `AGENTS.md` §5).

---

## D-01 — Workspace isolation is enforced by resource ownership and active relation, not by role alone
Status: Adopted

Reason: The specification states that global identity roles are not sufficient authorization. Every operation must also check workspace ownership or active mentoring participation.

Decision: The authorization model must validate both the authenticated user and the owning workspace context for each resource before the resource is returned or mutated.

## D-02 — Mentee can belong to multiple workspaces, but only one active mentoring context is active per session
Status: Adopted — **doprecyzowane przez D-08** (aktywny kontekst to stan UI, nie wejście autoryzacji)

Reason: The spec explicitly allows a mentee to be assigned to more than one workspace, and requires an active workspace choice after login.

Decision: The application must resolve the active mentoring relation at session or request time using an explicit `ActiveWorkspaceId` / active participant relation, not by trusting a resource ID alone.

## D-03 — AI-generated content remains draft until explicit publish
Status: Adopted

Decision: AI output is treated as draft content, must pass validation, can be edited manually, and is not visible as published content until an explicit publish action is taken by a mentor.

## D-04 — Operator-created mentors are the only onboarding path in MVP
Status: Adopted

Decision: The implementation must not add a public self-signup flow for mentors in the MVP.

## D-05 — P1 features remain explicitly out of scope
Status: Adopted

Decision: Delivery must exclude every P1 item from `docs/BACKLOG.md`.

## D-06 — Release gate includes cross-workspace isolation proof
Status: Adopted

Decision: Final acceptance requires automated tests and end-to-end validation proving workspace isolation in real workflow execution.

## D-07 — Central ownership of `AppDbContext`, migrations and DI remains locked to A1
Status: Adopted — **doprecyzowane przez D-09** (rejestracja modułów w plikach modułów)

Decision: No other agent may directly edit these shared files; changes must flow through contract review and A1 approval.

---

## D-08 — Model autoryzacji: WorkspaceId zawsze wyliczany z aktora lub zasobu
Status: Adopted (2026-10-17, użytkownik delegował decyzje na Tech Leada)

Kontekst: D-02 sugeruje `ActiveWorkspaceId` w sesji. Stan sesji w Blazor Server (circuit) jest łatwy do rozjechania z URL i nie może być dowodem uprawnień.

Decyzja:
1. Mentor: `WorkspaceId` = workspace, którego `OwnerUserId == actorUserId` (unikalny). UI nigdy nie przekazuje `WorkspaceId`.
2. Mentee: `WorkspaceId` wyliczany z zasobu (assignment → roadmap → mentorship) i weryfikowany `MenteeUserId == actorUserId`.
3. "Aktywny mentoring" (IAM-05) to wyłącznie stan nawigacji UI (route `/app/roadmap/{mentorshipId}` itd.). Mentee w dwóch workspace'ach ma dostęp do obu swoich relacji niezależnie od wybranego kontekstu.
4. Trasy mentora używają `MentorshipId`, nie `UserId` mentee (`/mentor/mentees/{mentorshipId}`).
5. Brak dostępu = `NotFound` (identyczny z nieistniejącym zasobem).
6. Insert: `WorkspaceId` dziecka kopiowany z zweryfikowanego rodzica; złożone FK w bazie.

Wpływ: `IWorkspaceAccessService` (AGENT_CONTRACTS §2.1); wszystkie serwisy.

## D-09 — Schema-first: A1 dostarcza pełny schemat MVP przed pracą równoległą
Status: Adopted (2026-10-17, użytkownik delegował decyzje na Tech Leada)

Kontekst: Spec §5 definiuje kompletny model. Równoległe dopisywanie encji/migracji przez A2–A4 = konflikty w `AppDbContext` i snapshotach migracji.

Decyzja:
1. A1 w M1 implementuje **wszystkie** encje z AGENT_CONTRACTS §3 i jedną migrację `InitialSchema`.
2. A2–A4 nie dotykają `MentorPath.Data`. Zmiana schematu = CR → A1.
3. A1 materializuje w M1 pliki `Contracts/` wszystkich modułów, puste `Add<X>Module()` i stuby komponentów cross-module; potem własność plików przechodzi na właścicieli modułów. Moduły rejestrują swoje serwisy we własnym `<X>Module.cs` — `Program.cs`/`AddMentorPathModules()` pozostaje u A1.
4. Encje to kształt danych; reguły domenowe w modułach.

Wpływ: A2, A3, A4 startują równolegle zaraz po M1, seedując dane przez `TestDataBuilder` — bez czekania na kod innych agentów.

## D-10 — Delty modelu danych względem MVP_SPEC §5
Status: Adopted (2026-10-17, użytkownik delegował decyzje na Tech Leada)

Kontekst: Spec EX-01 i §7.6 wymagają osobnych sekcji (scenariusz, wymagania, oczekiwany rezultat, kryteria, pytania), a tabela `Exercises` ma tylko `Instructions/Rubric/MentorGuide/Explanation`. `RoadmapItems` nie ma `WorkspaceId`, co uniemożliwia relacyjną kontrolę spójności workspace'u (spec §5.15). Brak nazwy wyświetlanej użytkownika.

Decyzja:
- `Exercises`: + `ScenarioMarkdown text NOT NULL`, + `ExpectedOutcomeMarkdown text NULL`, + `DiscussionQuestionsMarkdown text NULL`. `InstructionsMarkdown` = ograniczenia/wymagania.
- `RoadmapItems`: + `WorkspaceId uuid NOT NULL`.
- `AspNetUsers`: + `DisplayName varchar(200)`, + `CreatedAtUtc`.
- Alternatywne klucze `(WorkspaceId, Id)` i złożone FK `(WorkspaceId, ParentId)` dla: Exercise→Topic, Roadmap→Mentorship, RoadmapItem→Roadmap/Topic, Assignment→RoadmapItem/Exercise, Submission→Assignment, Attachment/Feedback→Submission.
- Partial unique: `QuizOptions(ExerciseId) WHERE IsCorrect`; `Submissions(AssignmentId) WHERE Status='PendingReview'`.
- `Topic.Difficulty`, `Mentorship.CurrentLevel` jako enumy zapisywane stringiem.

Alternatywa odrzucona: jeden `ContentJson jsonb` per typ — trudniejsza walidacja i zapytania, mniej czytelny kontrakt.

## D-11 — Przepływy zależne od e-maila
Status: Adopted (2026-10-17, użytkownik delegował decyzje na Tech Leada)

Kontekst: IAM-03 wymaga "odpowiedniej weryfikacji" e-maila; IAM-04 wymaga resetu hasła; jednocześnie "automatyczne powiadomienia e-mail" są P1, a zaproszenia mentor przekazuje sam.

Decyzja:
1. Zaproszenie można przyjąć tylko kontem, którego `NormalizedEmail` == `Invitation.NormalizedEmail`. Nowe konto: e-mail zablokowany na wartość z zaproszenia; posiadanie ważnego tokenu traktujemy jako potwierdzenie e-maila (`EmailConfirmed = true`). Istniejące konto z innym e-mailem: odmowa (`invitation.email_mismatch`) — mentor wysyła nowe zaproszenie. Brak zmiany e-maila w MVP.
2. Reset hasła: **transakcyjny e-mail SMTP** (`IEmailSender<AppUser>`), lokalnie Mailpit w docker-compose. To nie są "powiadomienia" P1 — wyłącznie e-mail bezpieczeństwa konta.
3. `create-mentor` wypisuje jednorazowy link ustawienia hasła (token resetu Identity) — operator przekazuje go mentorowi.

Alternatywa: brak SMTP, reset wyłącznie przez operatora (CLI `reset-password`) — taniej, bez dodatkowego procesora danych (RODO), ale brak samoobsługi wymaganej przez IAM-04.

Do zatwierdzenia: wybór dostawcy SMTP produkcyjnego (B17).

## D-12 — Semantyka statusów Mentorship
Status: Adopted (2026-10-17, użytkownik delegował decyzje na Tech Leada)

Kontekst: Spec definiuje `Active/Paused/Closed` bez zachowania.

Decyzja:
- `Active`: pełny dostęp.
- `Paused`: mentee — tylko odczyt (roadmapa, historia, feedback, refleksje dozwolone); brak nowych prób. Mentor — pełny dostęp, może oceniać oczekujące próby.
- `Closed`: mentee — brak dostępu do relacji (znika z listy). Mentor — tylko odczyt + ponowna aktywacja. Dane zachowane.
- Zmiana statusu: mentor na `/mentor/mentees/{mentorshipId}` (B04, A1). Ponowne zaproszenie mentee z relacją `Closed` w tym samym workspace → reaktywacja istniejącej relacji (unikalność `(WorkspaceId, MenteeUserId)`).

---

## D-13 — Walidacja draftu vs publikacji
Status: Adopted (2026-10-17, delegacja)

Zapis draftu: Title + limity długości + co najwyżej jedna poprawna opcja + brak HTML w danych z AI. Publikacja: pełna macierz z AGENT_CONTRACTS §5.2. Drafty AI muszą przejść pełną walidację już przy generowaniu (AI-02).

## D-14 — Ujawnianie odpowiedzi w quizie
Status: Adopted (2026-10-17, delegacja)

Przed wysłaniem DTO mentee nie zawiera `IsCorrect` ani wyjaśnień (gwarancja typu `MenteeExerciseDto`). Po próbie: wyjaśnienie wybranej opcji. Po próbie `Correct`: także `ExplanationMarkdown` ćwiczenia. Po `Correct` lub `Accepted` kolejne próby są blokowane.

## D-15 — Refleksja
Status: Adopted (2026-10-17, delegacja)

Dozwolona dla prób `Correct`, `Incorrect`, `Accepted`, `NeedsRevision` (nie `PendingReview`), tylko przez autora, raz na próbę, niemodyfikowalna. Dozwolona również przy mentorshipie `Paused`.

## D-16 — Reguły postępu
Status: Adopted (2026-10-17, delegacja)

Temat bez aktywnych wymaganych przypisań nie wlicza się do licznika ani mianownika (`CountsTowardsProgress = false`). `0/0` → 0% z komunikatem "brak wymaganych zadań". Opcjonalne przypisania nie blokują zaliczenia tematu.

## D-17 — Edycja roadmapy po publikacji
Status: Adopted (2026-10-17, delegacja)

Zmiany po publikacji są widoczne od razu (brak cofania publikacji). Usuwanie itemu/przypisania tylko bez submissions; w przeciwnym razie dezaktywacja (`IsActive = false`). Mentee nie widzi nieaktywnych elementów na roadmapie, ale zachowuje dostęp do historii prób przez `/app/assignments/{id}/history`. Inwariant publikacji sprawdzany tylko przy publikacji.

## D-18 — `RoadmapItem.MentorNoteMarkdown` jest widoczne dla mentee
Status: Adopted (2026-10-17, delegacja)

To notatka mentora do tematu na ścieżce mentee ("dlaczego ten temat"). Prywatne notatki to wyłącznie `Mentorship.MentorPrivateNotes` i `Exercise.MentorGuideMarkdown`.

## D-19 — AI: cykl życia, limit, dostawca
Status: Proposed (wybór dostawcy produkcyjnego — **APPROVAL REQUIRED przed włączeniem B07 na produkcji**)

- Po pozytywnej walidacji odpowiedzi od razu powstaje `Exercise` (Draft, Source=AI); `AiGenerationRequests.ExerciseId` wskazuje na niego. "Odrzuć" = twarde usunięcie draftu (FK SetNull zachowuje log).
- Limit: `Ai:MonthlyGenerationLimitPerWorkspace` (domyślnie 50), okno = miesiąc kalendarzowy UTC, liczone `Pending + Completed`.
- Adapter: `OpenAiCompatibleDraftGenerator` (HttpClient, structured output) + `FakeExerciseDraftGenerator`. Konkretny dostawca (OpenAI / Azure OpenAI EU / inny) zależy od RODO i kosztów.
- UI ostrzega mentora, by nie wpisywał danych osobowych w polu kontekstu.

## D-20 — Upload przez dedykowany endpoint HTTP
Status: Adopted (2026-10-17, delegacja)

Zgodnie z spec §8.2. Zapis do storage przed transakcją DB; kompensacja `DeleteAsync` przy błędzie. Osierocone obiekty po awarii procesu — akceptowane ryzyko MVP (sprzątanie = P1).

## D-21 — Strategia E2E
Status: Adopted (2026-10-17, delegacja)

Scenariusz release jako test integracyjny na prawdziwych serwisach + endpointach HTTP (Testcontainers + WebApplicationFactory), uruchamiany w CI. Playwright poza MVP; A5 dostarcza manualny smoke checklist UI.

## D-22 — Brak EF global query filters
Status: Adopted (2026-10-17, delegacja)

Te same tabele są czytane w kontekście mentora i mentee; filtr zależny od stanu circuit przy `IDbContextFactory` jest podatny na błędy. Zamiast tego: jawne sprawdzenie dostępu + jawne `Where(WorkspaceId)` + testy macierzy izolacji.

## D-23 — Zamiana `IProgressService.GetMenteeDashboardAsync` na kompozycję komponentów
Status: Adopted (2026-10-17, delegacja)

Spec §6.4 przypisuje dashboard mentee do Progress, a backlog B09 do A3. Rozwiązanie: `/app` (A3) osadza komponent `<RoadmapProgressSummary>` (A4), zasilany `GetRoadmapProgressAsync`. Brak współdzielonego pliku między A3 i A4.

## D-24 — Komendy administracyjne w binarium aplikacji
Status: Adopted (2026-10-17, delegacja)

`migrate`, `create-mentor`, `seed-dev`, `seed-demo` jako verby `MentorPath.Web` — jeden obraz, jedna konfiguracja.

## D-25 — Zadanie praktyczne: link i/lub plik
Status: Adopted (2026-10-17, delegacja)

Wymagane co najmniej jedno z: URL http(s), plik. Dozwolone oba. Uzasadnienie i confidence zawsze wymagane.

## D-26 — Archiwizacja
Status: Adopted (2026-10-17, delegacja)

Temat zarchiwizowany: ukryty domyślnie w katalogu, brak nowych ćwiczeń i nowych itemów roadmapy; istniejące działają. Ćwiczenie `Archived`: brak nowych przypisań; istniejące przypisania i historia działają.

## D-27 — Zaproszenia: reguły brzegowe
Status: Adopted (2026-10-17, delegacja)

Mentor nie może zaprosić własnego e-maila. Nowe zaproszenie dla tego samego e-maila w workspace wygasza poprzednie oczekujące (`ExpiresAtUtc = now`). Zaproszenie dla osoby z aktywną relacją w workspace → `invitation.already_member`. Użytkownik może być mentorem własnego workspace'u i mentee w innym.

## D-28 — UI tylko po polsku, bez resx
Status: Adopted (2026-10-17, delegacja)

Teksty UI bezpośrednio w `.razor`; kod, identyfikatory i `AppError.Code` po angielsku.

## D-29 — Biblioteki
Status: Adopted (2026-10-17, delegacja)

Dozwolone: Npgsql EF Core, ASP.NET Core Identity, Markdig, HtmlSanitizer, AWSSDK.S3, xUnit, Shouldly, Testcontainers, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.TimeProvider.Testing. Zakazane: MediatR, AutoMapper, FluentAssertions ≥ 8 (licencja komercyjna), generic repository. Nowy pakiet = CR do A1.

## D-30 — Repozytorium git
Status: Adopted — A1 wykonuje `git init`; zdalne repo GitHub po stronie użytkownika

Folder nie jest repozytorium git, a model pracy wymaga gałęzi/worktree i GitHub Actions. A1 wykonuje `git init` (gałąź `main`) w B01. Utworzenie zdalnego repozytorium GitHub i włączenie Actions — po stronie użytkownika.

---

## Change Requests

_(brak)_
