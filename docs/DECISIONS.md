# MentorPath — Decision Log

Statusy: **Adopted** (obowiązuje) · **Proposed — APPROVAL REQUIRED** (blokuje start pracy, której dotyczy) · **Proposed** (domyślnie przyjęte, jeśli brak sprzeciwu przy akceptacji M0) · **Superseded**.

Każda decyzja: kontekst → decyzja → wpływ. Nowe decyzje dopisujemy na końcu (append-only). Od D-41 nie ma Change Requestów ani agentów A0–A5 — zmiany schematu idą osobnym PR z etykietą `schema-change`. Wzmianki o A1–A5 i CR w starszych wpisach są historyczne.

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
Status: Superseded by D-41 (wcześniej: doprecyzowane przez D-09) — blokadę własności zastępuje CODEOWNERS + etykieta `schema-change`

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

`migrate`, `create-mentor`, `seed-dev`, `seed-demo` jako verby `MentorPath.Web` — jeden obraz, jedna konfiguracja. Dodane później: `export-user`, `anonymize-user` (D-42).

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

## D-31 — Lokalny storage S3: SeaweedFS zamiast MinIO
Status: Adopted (2026-10-17, implementacja)

Obraz `minio/minio` nie jest już dostępny publicznie (Docker Hub/quay odmawiają pobrania). Lokalnie używamy SeaweedFS z bramką S3 (`chrislusf/seaweedfs`, tożsamości w `infra/seaweedfs/s3.json`). Kod korzysta wyłącznie z `IFileStorage` + AWSSDK.S3, więc produkcyjnie działa z dowolnym dostawcą S3-compatible.

## D-32 — `TestDataBuilder` buduje dane przez serwisy aplikacji
Status: Adopted (2026-10-17, implementacja)

Zamiast bezpośrednich insertów (ARCHITECTURE §11) builder używa prawdziwych serwisów (zaproszenia, katalog, roadmapy, submissions). Dane testowe zawsze spełniają reguły domenowe; testy ograniczeń bazy wstawiają encje bezpośrednio przez `TestApp.Db`.

## D-33 — Smoke test przeglądarkowy (Playwright) jako opcjonalne narzędzie
Status: Adopted (2026-10-17) — rozszerza D-21

Release gate w CI to test `Release/ReleaseScenarioTests` + testy HTTP. Dodatkowo `tests/e2e/ui-smoke.js` przechodzi cały przepływ w prawdziwej przeglądarce (zaproszenie → AI → roadmapa → quiz → ADR → review → refleksja → upload → postęp → izolacja Mentora B). Uruchamiany ręcznie przed wydaniem; nie blokuje CI.

## D-34 — Jedna komenda danych demo: `seed-demo`
Status: Adopted (2026-10-17) — zastępuje `seed-dev` z ARCHITECTURE §9

Dane demo (2 mentorów, 2 mentees, mentee w dwóch workspace'ach, katalog, roadmapy, próba quizu) tworzone przez serwisy aplikacji. Działa tylko w `Development` lub z `--force`.

## D-35 — Szczegóły UI i współbieżności doprecyzowane w implementacji
Status: Adopted (2026-10-17)

- Klucze encji (Guid v7) nadaje aplikacja; EF ma `ValueGenerated.Never` dla encji domenowych.
- Status code pages (`/not-found`) nie są stosowane dla `/api/*` i `/health/*` (API zwraca 401/403/404 bez HTML).
- Domyślny nagłówek CSP Blazora (`frame-ancestors 'self'`) wyłączony — obowiązuje ostrzejszy nagłówek aplikacji (`'none'`).
- Formularz zadania praktycznego wysyła multipart przez `fetch` do endpointu z nagłówkiem antiforgery; quiz i ADR idą przez obwód Blazor.
- `AppUser.UserName = Email`; reset hasła i ustawienie hasła mentora używają tego samego mechanizmu tokenów Identity.

## D-36 — Pilotaż na MentorPath równolegle z Fazą 1 backlogu v2
Status: Adopted (2026-10-18, zaakceptowane przez użytkownika)

Research rekomenduje walidację „concierge” (np. Moodle) przed budową aplikacji. MVP już istnieje i pokrywa ten proces, więc pilotaż prowadzimy na MentorPath (Faza 0: wdrożenie, pomiar, friction log), a Fazę 1 budujemy w trakcie, korygując priorytety co tydzień.

## D-37 — `Topic` ewoluuje w `Competency`
Status: Adopted (2026-10-18, zaakceptowane przez użytkownika)

Zamiast drugiego katalogu: tabela tematów zostaje rozszerzona (prerequisites, „why it matters”, „you should be able to”, zasoby), w UI nazwa „Kompetencja”. Zachowuje ID, powiązania roadmap i ćwiczeń. Nazwy kompetencji niezależne od wersji .NET; wersja technologii jest atrybutem ćwiczenia. Alternatywa (osobna encja Competency obok Topic) odrzucona — dwa podobne pojęcia mylą mentora i komplikują roadmapę.

## D-38 — Domyślna rubryka procesu decyzyjnego
Status: Adopted (2026-10-18, zaakceptowane przez użytkownika)

6 kryteriów (Rozpoznanie ograniczeń, Alternatywy, Trade-offs, Ewolucja rozwiązania, Operacyjność, Komunikacja), skala 1–5 z opisami poziomów, edytowalne per workspace; ćwiczenie wybiera kryteria. `RubricMarkdown` zostaje jako kryteria specyficzne dla zadania. Przenosi z P1 do Fazy 1 „rozbudowane punktowe rubryki ocen”. Ocena wspiera, ale nie zastępuje jakościowego feedbacku (REV-04).

## D-39 — Status kompetencji nadaje mentor
Status: Adopted (2026-10-18, zaakceptowane przez użytkownika)

Stany: Not assessed, Learning, Practicing, Demonstrated, Needs revisit. System tylko sugeruje (np. wszystkie wymagane Accepted → sugestia Demonstrated). Postęp roadmapy = Demonstrated / aktywne kompetencje (obok dotychczasowego wyliczenia ukończenia zadań). Confidence pozostaje w skali 1–5 (research sugeruje 0–100; zostajemy przy 1–5 dla spójności z danymi i porównania z rubryką 1–5).

## D-40 — AI w review wymaga zmiany AI-05 i zgody mentee
Status: Adopted (2026-10-18, zaakceptowane przez użytkownika)

N50 wysyła treść rozwiązania mentee do dostawcy AI, co obecnie zabrania AI-05. Warunki: zmiana specyfikacji, umowa powierzenia z dostawcą, jawna zgoda mentee per mentoring, sugestie AI tylko dla mentora i zawsze edytowalne.

## D-41 — Proces pracy z agentami: planner + człowiek, bramki mechaniczne
Status: Adopted (2026-10-19, zaakceptowane przez użytkownika)

Kontekst: model A0–A5 (Tech Lead + 5 agentów zadaniowych) nie był egzekwowany technicznie (całość powstała sekwencyjnie w jednej sesji, CR nigdy nie użyto), reguły były zduplikowane w 8 plikach, a agenci B01–B17 opisywali pracę już zrobioną.

Decyzja:
- Agent „Tech Lead” zastąpiony przez agenta `mentorpath-planner` (issue z kryteriami akceptacji i macierzą izolacji) oraz człowieka (review, merge). Pozostają `mentorpath-security-reviewer` (niezależny przegląd, read-only) i `mentorpath-qa-tester` (tylko `tests/**`). Implementacja: Copilot cloud agent lub CLI/VS Code ze skillem `implement-backlog-item`.
- Instrukcje po angielsku, bez duplikacji: `AGENTS.md` (komendy, mapa, workflow, DoD), `.github/copilot-instructions.md` (niezmienniki), `.github/instructions/*.instructions.md` (reguły per ścieżka), `.github/skills/*` (procedury).
- Reguły egzekwuje kod, nie proza: `ConventionTests` (actorUserId, pokrycie `NotFound`, brak global filters, `TimeProvider`, `MarkupString`, własność zapisu), `ArchitectureTests`, CI (`has-pending-model-changes`, skan podatnych pakietów; CodeQL/secret scanning niedostępne dla prywatnego repo bez GHAS).
- Własność plików zastąpiona przez CODEOWNERS (Data, `Contracts/`, build, `.github`, testy izolacji). Zmiana schematu/Contracts = osobny PR z etykietą `schema-change`, merge przed PR funkcjonalnym. D-07 i mechanizm CR zastąpione.
- `docs/AGENT_CONTRACTS.md` (duplikat kodu) zastąpiony przez `docs/DOMAIN_RULES.md` (tylko reguły); dokumenty historyczne w `docs/archive/`.
- Ruleset na `main`: PR wymagany, check `build-test` wymagany, review CODEOWNERS.
- Tablica GitHub Project „MentorPath Backlog” (projekt użytkownika nr 6) jest miejscem śledzenia statusu: każda pozycja N-xx/T-xx to issue (#1–#42) z polami Phase, Priority, Schema change, Estimate; `Status`: Backlog → Planned → In progress → In review → Done. Karty przesuwa `scripts/board.sh` (best effort). `BACKLOG_NEXT.md` pozostaje planem produktowym.
- Każdy agent i skill kończy odpowiedź sekcją `## Next step` (jedna konkretna akcja człowieka z gotowym promptem).

Wpływ: pozycje N-xx realizowane jako issue → PR; reguły zmieniamy w jednym miejscu.

## D-42 — RODO minimum: eksport i anonimizacja mentee z CLI (N04)
Status: Proposed (2026-10-19; odpowiedzi właściciela w issue #4)

Decyzja:
- Moduł `Privacy` (`IPersonalDataService`), wywoływany wyłącznie z CLI (`export-user`, `anonymize-user`); bez aktora, więc metody są wyjątkiem od reguły `actorUserId` (jak `ProvisionMentorAsync`). Brak UI. Użycie poza `src/MentorPath.Web/Cli` blokuje `ConventionTests.Operator_services_are_used_only_by_the_CLI`. Metody nadal podlegają testowi pokrycia `NotFound` (nieznany podmiot → `privacy.not_found`).
- `export-user` zapisuje JSON wyłącznie do pliku: `--out <ścieżka>` jest wymagane, istniejący plik nie jest nadpisywany, uprawnienia `0600` (Unix). Nigdy na stdout — stdout jest strumieniem logów w produkcji (`DEPLOYMENT.md` §6); na stdout trafia tylko id użytkownika i liczniki.
- Podmiotem może być tylko mentee; właściciel workspace'u → `privacy.unsupported_subject` (erasure mentora dotyka cudzych relacji, D-04, D-27).
- Eksport: profil, relacje, próby, metadane załączników (klucze, bez treści plików), feedback, refleksje, zaproszenia na e-mail osoby. Bez `MentorPrivateNotes`, danych innych osób i sekretów Identity.
- Anonimizacja: konto (nazwa `Usunięty użytkownik`, e-mail `deleted-<id>@anonymized.invalid`, hasło i logowania usunięte, blokada), relacje `Closed` (D-12) z wyczyszczonym `Goal`/`Challenges`/`MentorPrivateNotes`, teksty prób, refleksji i trzech pól feedbacku wyczyszczone (werdykt i daty zostają), pliki usunięte, wiersze `SubmissionAttachments` usunięte, e-mail w zaproszeniach zastąpiony. Statystyki workspace'u (statusy, numery prób, confidence, czasy) bez zmian. Tabele Identity zmieniane wyłącznie przez `UserManager`.
- Idempotencja bez zmiany schematu: konto uznaje się za zanonimizowane wyłącznie, gdy e-mail jest dokładnie równy `deleted-{UserId:N}@anonymized.invalid` dla tego użytkownika — nigdy po samej domenie (zaproszenie akceptuje dowolny poprawny adres, więc prawdziwe konto może mieć domenę `anonymized.invalid`). Ponowne uruchomienie zawsze czyści treści i usuwa pliki (obejmuje też feedback wystawiony po pierwszym uruchomieniu); pomija tylko zmiany konta.
- Mentor może przywrócić relację `Closed → Active` (D-12); dla zanonimizowanego mentee jest to nieszkodliwe (konto zablokowane, brak PII) i nie jest blokowane.
- Próba `PendingReview` anonimizowanego użytkownika zostaje w kolejce mentora (puste treści); jej status się nie zmienia. Feedback wystawiony do niej później usuwa kolejne uruchomienie `anonymize-user` (procedura w `DATA_RETENTION.md`).
- Usuwanie plików przed zmianami w bazie; błąd magazynu → baza bez zmian, ponowne uruchomienie dokończy pracę.
- Retencja: relacja aktywna — dane do końca relacji; `Closed` — anonimizacja po 12 mies.; zaproszenia — 30 dni po wygaśnięciu; kopie DB 14 dziennych + 8 tygodniowych; prośby obsługiwane w 30 dni; wersjonowanie S3 — reguła cyklu życia dla nieaktualnych wersji (bez kodu). Szczegóły w `docs/DATA_RETENTION.md` (PR z implementacją).

- Błąd aktualizacji konta przez `UserManager` (np. walidacja Identity) → `privacy.account_update_failed` (Conflict), transakcja wycofana; kod nie jest wymieniony w komentarzu kontraktu (zmiana `Contracts/` poza tym PR).
- Kody wyjścia CLI: 0 sukces, 1 błąd (w tym nieznany użytkownik, istniejący plik `--out`), 2 niepoprawne argumenty.

Wpływ: nowy wpis własności zapisu `Privacy` (DOMAIN_RULES §5, w tym tabele Identity), wyjątek twardego usuwania załączników (§3), test `Operator_services_are_used_only_by_the_CLI`.

## D-43 — Rubryka procesu decyzyjnego: model danych i reguły (N10)
Status: Proposed (2026-10-20; zakres i odpowiedzi w issue #5)

Doprecyzowuje D-38. Zastępuje REV-04 („bez obowiązkowej skali”) dla ćwiczeń recenzowanych: ocena punktowa jest obowiązkowa, ale nie zastępuje jakościowego feedbacku (Strengths/Improvements/Challenge pozostają wymagane).

Decyzja:
- Tabele: `RubricCriteria` (per workspace: nazwa, pozycja, `IsActive`, opisy poziomów 1–5), `ExerciseCriteria` (wybór kryteriów przez ćwiczenie), `FeedbackCriterionScores` (ocena 1–5 + komentarz widoczny dla mentee, per kryterium). Własność zapisu: Catalog (`RubricCriteria`, `ExerciseCriteria`), Reviews i Privacy (`FeedbackCriterionScores`).
- Migracja zasiewa 6 domyślnych kryteriów dla każdego istniejącego workspace'u i wybiera wszystkie dla każdego ćwiczenia recenzowanego (typ ≠ quiz). `RubricMarkdown` pozostaje bez zmian jako opis zadania. Treść domyślnych opisów poziomów jest szkicem do akceptacji właściciela.
- Workspace utworzony po migracji dostaje domyślne kryteria leniwie i idempotentnie przy pierwszym użyciu w module Catalog (bez zapisu między modułami z `Workspaces`).
- Nowe ćwiczenie recenzowane domyślnie wybiera wszystkie aktywne kryteria; mentor może zmienić wybór (min. 1).
- Review wymaga dokładnie jednej oceny na każde wybrane kryterium; ocena jest niezmienna po zapisie (jak feedback), zapisana w tej samej transakcji co feedback.
- Kryteria można tylko dezaktywować, nie usuwać. Brak snapshotu: edycja nazwy/opisu zmienia sposób wyświetlania także starszych ocen (świadomie, najprostsze rozwiązanie). Feedback sprzed N10 nie ma ocen.
- DTO mentee zawiera nazwę kryterium, ocenę, komentarz i opis przyznanego poziomu; nie zawiera danych prywatnych mentora. Brak prywatnych notatek per kryterium w N10.
- Zmiana kontraktów w PR schematu ogranicza się do kształtu DTO (opcjonalne parametry końcowe); nowy `IRubricService` powstaje w PR z serwisami.

Wpływ: nowe tabele i `AK (WorkspaceId, Id)` na `MentorFeedback`; DOMAIN_RULES §3 i §5; `WriteOwnership` w `ConventionTests`.

---

## Change Requests

Mechanizm CR zniesiony przez D-41. Zmiany schematu i `Contracts/` — osobny PR z etykietą `schema-change` i review CODEOWNERS.
