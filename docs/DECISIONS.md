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

Doprecyzowanie (PR z serwisami):
- Review wymaga ocen dla kryteriów wybranych przez ćwiczenie **i** aktywnych; dezaktywowane kryterium przestaje być wymagane, a wcześniejsze oceny pozostają widoczne. Gdy zbiór jest pusty (np. historyczne ćwiczenie), ocen nie wymagamy i `Scores` w historii jest `null`.
- Komentarz do oceny kryterium jest opcjonalny (widoczny dla mentee, Markdown, limit jak inne pola).
- Nie można dezaktywować ostatniego aktywnego kryterium (`rubric.last_active_criterion`); nazwa kryterium jest unikalna w workspace (`rubric.name_taken`).
- `ExerciseContent.CriterionIds == null` przy edycji oznacza „bez zmian”; przy tworzeniu — wszystkie aktywne. Pusta lista jest dozwolona w szkicu, ale blokuje publikację (typ ≠ quiz). Quiz nie ma kryteriów.
- Kryterium z innego workspace'u w ocenie lub w wyborze ćwiczenia → `NotFound`; kryterium nieaktywne w wyborze ćwiczenia → walidacja.
- Wyjątek od zasady „kontrakty w PR schematu”: `IRubricService`, `UpdateRubricCriterionRequest` i `CriterionScoreExport` dodane w PR z serwisami (wymaga jawnej akceptacji CODEOWNERS). Uzasadnienie: `ConventionTests` wymagają testu `NotFound` dla każdej metody `I*Service`, więc samego interfejsu nie da się zmergować bez implementacji; kształty DTO, które zależą od schematu, były już w PR schematu (#52).
- Eksport danych osobowych zawiera oceny kryteriów; anonimizacja czyści komentarze do kryteriów.

## D-44 — Wizualna mapa roadmapy renderowana po stronie serwera (SVG), bez nowych pakietów (N28)
Status: Proposed (2026-10-21)

Decyzja:
- Mapa „gdzie jestem / co zostało” to komponent Razor `RoadmapMap` rysujący SVG po stronie serwera; brak biblioteki wykresów i JS interop (zakaz nowych pakietów bez ADR). Układ liczy czysta funkcja `RoadmapMapLayout` (testowana jednostkowo).
- Dane mapy zwraca `IRoadmapMapService` w module `Progress` (Progress już czyta roadmapę, przydziały i zgłoszenia; `Roadmaps` nie może zależeć od `Progress`). Serwis jest tylko do odczytu — własność zapisu bez zmian.
- Mentee widzi wyłącznie opublikowaną roadmapę i aktywne pozycje; bez notatek mentora i bez pozycji wycofanych. Mentor (właściciel workspace'u) widzi także szkic i pozycje wycofane. Brak dostępu lub nieistniejący mentoring → `NotFound`.
- Stan węzła wynika z przydziałów wg reguł D-16 (`ProgressCalculator`); „Tu jesteś” = pierwszy temat wymagający poprawki, w przeciwnym razie pierwszy zaliczany i niezaliczony. Widok mapy jest domyślny dla mentee i przełączalny z listą; u mentora to zakładka obok edytora.

## D-45 — Poddrzewa tematów: relacja rodzic–dziecko w katalogu (N29, plan)
Status: Proposed (2026-10-21)

Decyzja (do realizacji w osobnych PR-ach: schemat, serwisy/UI):
- Hierarchia na poziomie katalogu: `Topic.ParentTopicId` (opcjonalny; jeden rodzic; głębokość ≤ 3; ten sam workspace — złożony FK; brak cykli). Tylko drzewo; prerequisites/DAG pozostają w N20.
- Podtemat trafia do roadmapy mentee wyłącznie jawnie (mentor dodaje go jak każdy temat). Postęp (D-16) nie zmienia się; ewentualna agregacja rodzica jest tylko wyświetlana.
- Podtemat spoza planu mentee jest na mapie szary („poza planem”), tylko tytuł, i tylko pod tematem, który jest na roadmapie — bez ujawniania treści katalogu.

Doprecyzowanie (PR schematu):
- Migracja `AddTopicParent` jest addytywna: nullable kolumna (istniejące tematy zostają korzeniami), złożony FK `(WorkspaceId, ParentTopicId)` → AK `(WorkspaceId, Id)` z `Restrict`, check `ParentTopicId <> Id`, indeks `(WorkspaceId, ParentTopicId)`. Cykle (wielokrokowe) i głębokość ≤ 3 nie są wyrażalne w bazie — waliduje je `TopicService` w PR z serwisami.
- Kontrakty: opcjonalne parametry końcowe `ParentTopicId` w `UpsertTopicRequest`, `TopicListItem`, `TopicDetail` (kompatybilne wstecz). Do czasu PR z serwisami serwis ignoruje to pole.

Doprecyzowanie (PR kontraktu mapy):
- `RoadmapMapContracts`: `MapNodeState.NotInPlan` (dopisany na końcu enuma) oraz opcjonalne parametry końcowe `TopicId`, `ParentTopicId` w `RoadmapMapNodeDto` (`ParentTopicId` = najbliższy przodek widoczny na mapie). Zmiana wyłącznie addytywna; serwis i UI wypełniają je w osobnym PR (wcześniej `TopicId` = `default`, `ParentTopicId` = null).

## D-46 — Odświeżenie warstwy wizualnej i układu aplikacji (Impeccable)
Status: Proposed (2026-10-20; tylko prezentacja, bez zmian schematu i `Contracts/`)

- Zakres: nowy shell aplikacji (zwijana szyna nawigacji zależna od roli, górny pasek z okruszkami i menu użytkownika, przełącznik mentoringu dla mentee, dostępna szuflada na mobile zamiast „checkbox hack”), tokeny CSS (`tokens.css`, `shell.css`, `components.css` zamiast `app.css`), wspólne komponenty (`Page`, `PageHeader`, `Section`, `DataList`/`DataRow`, `StatTile`, `SplitView`, `Tabs`, `Icon`) oraz przeniesienie stron mentora, mentee i konta na nowy układ. Bootstrap 5 zostaje; jego komponenty są przestylowane tokenami.
- Narzędzie: skill Impeccable (projektowo, `.github/skills/impeccable`) z hookiem detektora anty-wzorców; `PRODUCT.md` i `DESIGN.md` opisują kierunek. Instalacja dotyka `.github/` (CODEOWNERS) i jest w osobnym commicie.
- Kierunek: chłodne, stonowane tło z odcieniem teal, jeden kolor akcentu, statusy zawsze ze słowem i kropką (nie sam kolor), brak ciemnego motywu w MVP. Czcionki IBM Plex (Sans/Serif/Mono) hostowane lokalnie z `wwwroot/fonts` (licencja OFL w katalogu) — bez CDN i bez nowego pakietu NuGet.
- Shell jest renderowany statycznie (SSR) — `Routes.razor` nie ma trybu renderowania — więc zwijanie szyny, szuflada i menu to lekki JS oparty o atrybuty `data-*` na `<html>` (stan szyny w `localStorage`, bez danych osobowych).
- Aktywny mentoring w przełączniku mentee jest wyliczany z adresu URL (`/app/roadmap|progress/{id}` lub `?mentorshipId=`), z domyślnym pierwszym; to wyłącznie stan nawigacji (por. D-08), nie uprawnienie — dostęp nadal sprawdzają serwisy. Ograniczenie: na stronie zadania linki „Roadmapa”/„Postęp” w szynie wskazują pierwszy mentoring.
- Wszystkie strony zachowują `[Authorize]`, trasy, teksty po polsku i selektory używane przez `tests/e2e/ui-smoke.js`. Komponenty nie używają `AppDbContext`.

---

## D-47 — Azure zamiast S3: hosting, pliki i klucze Identity
Status: Adopted (2026-10-22, na życzenie właściciela produktu); zastępuje D-31 i wymaganie S3 w MVP §6.1

- Produkcja: jeden kontener App Service Linux, Azure Database for PostgreSQL 17, prywatny Azure Blob Storage. Nie wymagamy AWS ani kompatybilności S3. Lokalnie Azurite zastępuje SeaweedFS; `IFileStorage` i klucze `ws/{workspaceId}/sub/{id}` pozostają bez zmian, bez migracji schematu i `Contracts/`.
- ADR zależności: usuwamy `AWSSDK.S3` i nieużywany `Testcontainers.Minio`; dodajemy oficjalne `Azure.Storage.Blobs`, `Azure.Identity` oraz `Azure.Extensions.AspNetCore.DataProtection.Blobs`. SDK zapewnia streaming, anulowanie, retry oraz obsługę managed identity bez własnego podpisywania REST. Zmiany pakietów wymagają review CODEOWNERS. Testy adaptera używają Azurite przez istniejący Testcontainers.
- `Storage:Provider=AzureBlob` domyślnie. Brak konfiguracji to błąd, nie fallback do pamięci. `InMemory` jest jawnym wyborem testowym i zabronione w Production. `ServiceUri` używa `DefaultAzureCredential`; connection string ma pierwszeństwo i służy lokalnemu emulatorowi. Produkcyjnie managed identity + rola `Storage Blob Data Contributor` ograniczona do kontenera.
- Kontenery tworzy operator, aplikacja domyślnie tylko w Development (`Storage:EnsureContainerOnStartup`). Błąd tworzenia przerywa start. Pliki pozostają prywatne, pobierane wyłącznie przez autoryzowany endpoint. Tylko `BlobNotFound` staje się `FileNotFoundException`; błąd kontenera/poświadczeń nie jest maskowany.
- Klucze Data Protection w osobnym prywatnym blobie (`DataProtection:BlobUri`, pierwszeństwo przed `KeysPath`), ze stałym application name `MentorPath`, wspólne dla web i CLI. Azure szyfruje storage at rest; klucze nie mają dodatkowego szyfrowania aplikacyjnego. Dostęp do kontenera kluczy daje możliwość odszyfrowania cookies/tokenów — tylko tożsamość aplikacji i zaufany operator. Key Vault w instrukcji chroni sekrety konfiguracji, nie szyfruje key ring.
- Bazowy wariant wdrożenia: publiczne endpointy TLS, baza ograniczona firewallem do IP operatora i App Service; storage prywatny przez RBAC (nie prywatny endpoint sieciowy). To kompromis prostoty MVP, nie wariant pełnej izolacji sieciowej. Przed większym wdrożeniem rozważyć VNet/private endpoints i szyfrowanie key ring przez Key Vault.
- Retencja Blob: soft delete blobów/kontenerów 30 dni, wersjonowanie wyłączone. Włączenie wersjonowania wymaga polityki usuwania poprzednich wersji po 30 dniach. Nie wygaszamy bieżących załączników ani kluczy Identity. Migracja istniejących obiektów S3 jest operacją operatora przed przełączeniem, z zachowaniem kluczy i weryfikacją bajtów; nie usuwamy starego magazynu automatycznie.
- Instrukcja: `AZURE_DEPLOYMENT.md` (zasoby, role, sekrety Key Vault, build, migracje, pierwszy mentor, backup i rollback). Nie dodajemy płatnych zasobów, automatycznego deployu ani nowego workflow CI przez samą zmianę kodu.

## D-48 — ACS Email przez SMTP: wybór właściciela i ograniczenie cyklu życia
Status: Proposed (2026-10-22); właściciel wybrał ACS Email, ale ostrzeżenie o wycofaniu wymaga akceptacji przed provisioningiem

- Właściciel chce Azure Communication Services Email, aby utrzymać konfigurację i rozliczenia w Azure. Wariant konfiguracyjny korzysta z istniejącego `SmtpEmailSender`: `smtp.azurecomm.net:587`, STARTTLS, użytkownik SMTP ACS powiązany z dedykowaną aplikacją Entra i jej sekret w Key Vault. Bez nowego SDK, zmian schematu i `Contracts/`.
- Podczas weryfikacji oficjalnej dokumentacji ujawniono wycofanie ACS Email 30 września 2028. Microsoft odradza nowe integracje; FAQ dopuszcza obecnie nowe wdrożenia, ale ich dostępność może zmienić się w dowolnym momencie. Nie zakładamy, że rejestracja będzie dostępna w danej subskrypcji. Źródło: https://learn.microsoft.com/azure/communication-services/acs-retirement-and-breaking-changes-guide#acs-email.
- Instrukcja przedstawia żądany wariant wyłącznie jako tymczasowy, z obowiązkiem migracji przed terminem wycofania. Akceptacja właściciela po zapoznaniu się z tą informacją pozostaje otwarta; alternatywą jest wspierany dostawca SMTP na tym samym adapterze.
- Zasoby: Email Communication Services + Communication Services w tej samej geografii danych, połączona domena (Azure-managed na pilotaż albo własna po weryfikacji DNS), dedykowana rejestracja Entra i uprawnienia ograniczone do zasobu Communication Services. Managed identity aplikacji pobiera sekret z Key Vault, ale nie zastępuje uwierzytelnienia SMTP. Operator odpowiada za rotację i alert przed wygaśnięciem sekretu.
- Zakres zachowania pozostaje D-11: reset hasła dla istniejących kont; bez automatycznej wysyłki zaproszeń i linków pierwszego ustawienia hasła mentora. Samo potwierdzenie formularza resetu nie dowodzi dostarczenia wiadomości — wymagany test odbioru. Nie utworzono zasobów ani nie wysłano wiadomości podczas zmiany dokumentacji.

## D-49 — Formularze na stronach InteractiveServer są wyłącznie interaktywne
Status: Adopted (2026-10-22, poprawka błędu zaproszenia mentee)

- Strony `InteractiveServer` są prerenderowane. `EditForm` z `FormName` renderuje zwykły `<form method="post">`; wysłany przed podłączeniem obwodu Blazor (lub przy zerwanym obwodzie) robi natywny POST bez `[SupplyParameterFromForm]`, więc serwer waliduje pusty model („Podaj e-mail”).
- Nie obsługujemy ścieżki SSR dla takich formularzy: nowa instancja komponentu po podłączeniu obwodu zgubiłaby jednorazowo pokazywane dane (np. link zaproszenia). Zamiast tego: bez `FormName`, przycisk submit wyłączony do `RendererInfo.IsInteractive`.

## D-50 — Obraz Dockera: publish z ponownym restore
Status: Adopted (2026-10-22, poprawka braku interaktywności na Azure)

- Restore wykonany tylko na plikach `.csproj` (warstwa cache) nie dodaje niejawnego pakietu `Microsoft.AspNetCore.App.Internal.Assets`, bo SDK nie widzi jeszcze komponentów Razor. `dotnet publish --no-restore` publikował więc obraz bez `_framework/blazor.web.js` (404), a cała aplikacja na Azure działała bez interaktywności.
- `dotnet publish` w `Dockerfile` wykonuje restore ponownie po `COPY src/`; wcześniejszy restore zostaje jako cache pakietów.

## D-51 — Ręcznie uruchamiane wydania Azure przez GitHub Actions
Status: Proposed (2026-10-22); workflow wymaga review CODEOWNERS

- Na życzenie właściciela dodajemy `workflow_dispatch` dla istniejącej instalacji Azure. Bez automatycznego wdrożenia po pushu, provisioningu zasobów ani zmian schematu/kontraktów w tym PR. Tylko `main`, środowisko GitHub `production` z ograniczeniem gałęzi; serializacja wydań bez anulowania migracji.
- Uwierzytelnienie OIDC do dedykowanej tożsamości Entra, bez sekretu klienta/publish profile. Osobno ograniczone uprawnienia do ACR, jednej aplikacji, reguł firewalla jednego serwera, dwóch sekretów bazy i prywatnego kontenera backupów. Bez dostępu do key ring, załączników i SMTP. Rola firewalla nie może ograniczyć uprawnień do prefiksu nazwy reguły; kod usuwa wyłącznie regułę własnego uruchomienia.
- Release build i testy jednostkowe/integracyjne dla dokładnego SHA, obraz Linux/amd64 z tagiem SHA + run ID + attempt i weryfikacją `blazor.web.js`. Lokalnie publikowany CLI migracji pochodzi z tego samego SHA; wywołanie `migrate` zapewnia także role Identity.
- Przed maintenance: logiczny backup PostgreSQL 17 przez TLS `verify-full`, kontrola czytelności i zapis do prywatnego `deployment-backups` (szyfrowanie Azure at rest). Retencja checkpointów wydań 90 dni, polityka wyłącznie dla ich prefiksu; nie zastępuje regularnych backupów, testu odtworzenia ani niezależnej kopii z D-47.
- Maintenance: zatrzymanie aplikacji, migracje przez nieadministracyjny login, zmiana obrazu, start i ograniczone czasowo sprawdzenia HTTP. Bez automatycznego cofania obrazu/schematu przy błędzie — możliwa częściowa migracja wymaga decyzji operatora. Cleanup `always()` usuwa regułę IPv4 runnera i lokalny backup; utrata runnera wymaga ręcznego sprawdzenia. Nie zmieniamy runtime SMTP/AI, kluczy Identity ani uprawnień domenowych.

## D-52 — Shared topic preparation resources, contextual access and external video players (N23 slice)
Status: Adopted (2026-10-22, approved preparation-resource plan); implementation split into schema/contracts and feature PRs

Context: topic descriptions and personal mentor notes currently appear only in the roadmap list, while the default map leads directly to assignments. The owner approved a focused N23 slice, not new competency content fields or a standalone library.

Decision:
- Store an ordered `TopicResources` list on the existing Topic: workspace, topic, title (200), absolute HTTPS URL (2000), Article/Video kind, optional plain-text description (1000), and nonnegative position. Catalog owns writes; composite topic/workspace FK and Restrict deletes preserve tenant integrity. Existing topics start with no resources.
- The owner can atomically replace the list, including removing resources. Derive workspace and contiguous positions server-side; serialize replacements on the topic row so concurrent saves are whole-list last-save-wins, not merged lists. Resource IDs need not survive replacement and are not referenced by other records.
- Saved edits are immediately visible across eligible roadmap placements of that topic. Personal guidance remains `RoadmapItem.MentorNoteMarkdown`; resources have no draft/publish, versioning, watch/completion state or progress effect.
- The mentee page is scoped to `/app/roadmap/{mentorshipId}/topics/{roadmapItemId}`, with links from both map and list. Check the exact mentorship/item relationship, Published roadmap, active item and participating mentee; Active and Paused mentorships permit reading, Closed does not. Assigned archived topics stay readable. Return only safe topic/resources, that item's personal note and active assigned exercises, never the catalog exercise list. No new exercise blocking.
- Declare focused `ITopicResourceService` (Catalog) and `ITopicPreparationService` (Roadmaps) contracts in the schema PR. Do not change existing implemented interfaces or add throwing stubs. Service implementations, DI, access checks, validation and isolation tests arrive only after the prerequisite merges.
- Video links from YouTube and Vimeo may use a typed, click-to-load iframe component in the feature PR, with validated exact provider hosts/IDs and a permanent external link. Other providers remain links. Do not allow raw HTML in Markdown or arbitrary iframe sources; do not fetch metadata or remote thumbnails.
- Use YouTube privacy-enhanced embeds and Vimeo DNT, preserving validated unlisted hashes. Load nothing from the provider before the viewer clicks, explain third-party contact, disable autoplay, preserve the origin referrer needed for playback and allow only the two player origins in `frame-src`. Privacy modes do not guarantee zero cookies/tracking. Embedding permissions, privacy and availability are controlled by the provider; playback is not guaranteed.
- This is external learning-resource presentation, a narrow refinement of the backlog's video exclusion, not a hosted video/course platform. No SDK/API credentials, uploads, playlists, analytics, lessons, full competency redesign or new packages. Dedicated "Why it matters" / "You should be able to" fields remain deferred.

Impact: additive schema and typed contracts first (`schema-change`, human CODEOWNERS merge), then Catalog/Roadmaps services and mentor/mentee UI in a separate feature PR. Markdown sanitization, assignment access and progress rules remain unchanged.

## Change Requests

Mechanizm CR zniesiony przez D-41. Zmiany schematu i `Contracts/` — osobny PR z etykietą `schema-change` i review CODEOWNERS.

## D-53 — Optional rationale in mentee topic context (N23)
Status: Adopted (2026-10-22, approved N23 follow-up plan)

Context: the topic already has a general `DescriptionMarkdown` and `LearningObjectivesMarkdown`. N23 calls for a distinct “Why it matters” rationale and a mentee-facing “You should be able to” section without replacing the general description.

Decision:
- Add optional `Topic.WhyItMattersMarkdown` as a separate Markdown field. Existing topics remain valid with no rationale, and the mentor may leave it blank.
- Keep `DescriptionMarkdown` as the general description. Reuse `LearningObjectivesMarkdown` as “You should be able to”; do not add a duplicate field.
- On the contextual mentee topic page, show the general description and learning objectives, and show the rationale only when it is non-empty. Render each value with the shared Markdown component.
- These fields do not change topic access, assignment eligibility, submission rules, progress, or the D-52 resource/player behavior.

Impact: the rationale column and optional trailing contract properties were merged in schema-change PR #70. This feature change implements mentor form persistence and mentee projection/rendering.
This decision supersedes D-52's deferral for these two context fields only; the resource/player slice and other exclusions remain unchanged.

## D-54 — Activity events and phase-0 mentor insights (N02)
Status: Adopted (2026-10-26, owner-confirmed event and anonymization behavior; reporting and ownership choices delegated)

Decision:
- Add append-only `ActivityEvents` records for `assignment_opened`, `review_opened` and `review_submitted`. Persist workspace, assignment, optional submission, mentee and mentor IDs, UTC occurrence time, optional review duration and an `ExceedsTwoHours` flag. Do not persist answer or feedback content. Keep mentee and mentor IDs in these records after mentee anonymization; N02 adds no time-based event purge.
- Record only the first authorized `assignment_opened` per assignment and the first authorized `review_opened` per pending submission. Record `review_submitted` only after feedback is successfully saved. Its duration is measured from the first review opening to feedback submission; retain the actual duration and flag durations strictly greater than two hours. Do not cap or exclude flagged durations.
- If feedback is submitted without a prior persisted `review_opened` event, record the opening at the feedback timestamp. This avoids inventing an unobserved start time; the resulting measured duration is zero.
- `Submissions` owns `assignment_opened` writes, `Reviews` owns review lifecycle event writes, and `Progress` reads events to calculate metrics without writing them.
- The insights window is the rolling 30 days ending at the current UTC time. Conversion metrics use mature cohorts only: assignment-open cohorts at least 14 days old and `NeedsRevision` feedback cohorts at least 7 days old. Incomplete cohorts are excluded until their full conversion window elapses.
- N02 includes only the four phase-0 metrics in `docs/BACKLOG_NEXT.md` §6. Later-phase metrics and engagement metrics are out of scope.

Impact: add `ActivityEvents` with workspace-consistent foreign keys, event-specific payload checks and filtered unique indexes. Add the mentor insights application contract in the schema-change PR; implement event capture, queries and UI only after that PR is merged. Update `DOMAIN_RULES.md`, `DATA_RETENTION.md` and the integration-test write-ownership convention.

## D-55 — Playwright smoke is a required CI gate (T01)
Status: Adopted (2026-10-26, confirmed by the user)

Context: D-33 made the full-flow Playwright browser smoke an optional manual pre-release check outside CI. T01 adds this existing smoke test to the continuous integration workflow.

Decision:
- Run the smoke test as a required CI job for every pull request and every push to `main`. A setup, seed, readiness, or browser-test failure fails the workflow.
- Run it against a disposable containerized application stack with PostgreSQL, Azurite, Mailpit, and the web app. Apply migrations and seed only demo data; remove the stack and its volumes at job completion.
- Reuse `tests/e2e/ui-smoke.js`, including its existing cross-workspace isolation assertion. Do not use production services or data.
- Retain browser failure screenshots as CI artifacts. The smoke remains runnable locally as documented.

Impact: this supersedes D-33's manual-only and non-blocking CI policy. No application schema, service contract, or product behavior changes.

## D-56 — Global product feedback and redaction (N03)
Status: Adopted (2026-10-26, confirmed by the user)

Decision:
- Mentors and mentees can submit global product feedback without associating the row with a workspace. Submission authorization still requires a real workspace relationship, checked through `IWorkspaceAccessService`: a mentor owns a workspace or a mentee has a mentorship in any status, including `Closed`. Identity role alone is insufficient.
- Store the author user ID, required plain-text message (maximum 2000 characters), optional page path without query string (maximum 2000 characters), and UTC creation time. Do not store a workspace ID, author role, category, or rate-limit state. The operator follows up using the author ID.
- The operator lists feedback through the CLI only, newest first; users cannot read submissions back in the web app.
- Anonymization retains the feedback row and author ID but blanks the message. Database storage permits an empty message for that redacted state; submission validation rejects empty or whitespace-only text.
- Place “Uwaga do produktu” in the top-bar user menu. No rate limit is added for the MVP.

Impact: add a global `ProductFeedback` table in a schema-change PR. `Feedback` owns inserts and `Privacy` owns message redaction. Update `DOMAIN_RULES.md`, `DATA_RETENTION.md` and write-ownership convention tests; implement submission, operator CLI and privacy behavior only after the schema PR is merged.

## D-57 — Product feedback page context and export schema (N03)
Status: Adopted (2026-10-27, clarification during PR review)

Decision:
- Store `PageContext` only when it is a local application path beginning with one `/`; reject network-path forms (`//` and `/\`), external URLs, and control characters. Remove query strings and fragments before storage.
- Anonymization clears the feedback message but retains the local page path, author ID and timestamp for product context and operator follow-up. The privacy export includes this context.
- Increment the personal-data export `schemaVersion` to 2 when adding the feedback collection.

Impact: page context cannot carry a query-string secret or external destination, but may contain route identifiers and remains associated with the retained author ID after anonymization. Document this retention in `DATA_RETENTION.md`.

## D-58 — CSP `script-src 'self'` i szyfrowanie key ringu Data Protection (T02)
Status: Adopted

- CSP zawiera `script-src 'self' 'nonce-<per request>'` (nonce generowany per żądanie, tylko dla inline `<script type="importmap">` Blazora). Jedyny inline script (stan rail w `App.razor`) przeniesiony do `wwwroot/js/rail-init.js`. Test sprawdza nagłówek i że jedynym inline `<script>` jest import map z nonce. `style-src` bez zmian (poza zakresem).
- Klucze Data Protection szyfrowane certyfikatem X.509 (`DataProtection:CertificatePath`, `DataProtection:CertificatePassword`; PKCS#12) przez `ProtectKeysWithCertificate`, bez nowego pakietu. W Production brak certyfikatu przy trwałym key ringu przerywa start.
- Istniejące niezaszyfrowane klucze nadal się odczytują; nowe są szyfrowane. Utrata certyfikatu = utrata kluczy (jak utrata key ringu). Certyfikat i hasło przechowuj w Key Vault/sekretach, wspólne dla web i CLI.
- Uzupełnienie T02: lokalny pełny stos i smoke CI pozostają w Production. Przed startem generują lokalny certyfikat PKCS#12 przez `scripts/create-local-dp-certificate.sh`, montowany jako read-only Compose secret do web i CLI. `.local/` jest ignorowany przez Git i Docker; certyfikat nie trafia do obrazu ani repozytorium. Zmiana workflow CI jest ograniczona do generowania tego certyfikatu i wymaga przeglądu CODEOWNERS. Nie wyłączamy walidacji Production na potrzeby smoke.
- Uzupełnienie po review T02: wymóg certyfikatu w Production i zastosowanie skonfigurowanego certyfikatu nie zależą od ustawienia `KeysPath`/`BlobUri`; obejmują też magazyn wybrany domyślnie przez framework. Stała nazwa aplikacji `MentorPath` dotyczy również tej ścieżki (wcześniejsze cookies/tokeny hostów bez jawnej konfiguracji magazynu mogą wymagać ponownego wystawienia). Certyfikat nie zapewnia trwałości magazynu: operator nadal odpowiada za trwałe, wspólne przechowywanie kluczy web i CLI.

## D-59 — Background job infrastructure and database coordination (T05)
Status: Adopted (2026-10-28, confirmed by the user)

Decision:
- T05 provides reusable hosting infrastructure only. It does not include a concrete reminder, email, cleanup, or tenant-data job, queue, or event bus.
- Jobs are registered through dependency injection and share one configurable interval, hosted by `BackgroundService` with `PeriodicTimer`.
- Each scheduler tick uses a PostgreSQL session-level advisory lock shared across processes. Contending workers use a non-blocking try-lock and skip that tick. The lock is explicitly released on normal completion; PostgreSQL releases it when the owning session/process is lost. If acquisition fails ambiguously, discard the connection pool so an unknown lock holder cannot be reused. No persisted lock/lease table is added.
- A failed job is logged and does not prevent other registered jobs or later ticks from running. Host shutdown cancellation propagates through the in-flight job.

Impact: future job consumers register into the shared schedule and must account for skipped ticks while another process holds the lock. Individual job failures are visible in logs but do not stop the scheduler. T05 requires no schema migration or `Contracts/` prerequisite.

## D-60 — Cleanup osieroconych załączników (T03)
Status: Adopted

- The scheduled cleanup compares current objects in the private attachment container with all `SubmissionAttachments.ObjectKey` values. It deletes only unreferenced objects whose Azure last-modified time is at least 24 hours old, and rechecks database references before deletion.
- Complete the blob inventory and database reference reads before deleting any candidate. Propagate storage/database errors and cancellation; an incomplete inventory must never trigger partial cleanup.
- T03 registers the cleanup with the shared T05 background-job scheduler and PostgreSQL advisory lock. No schema or `Contracts/` change is required.

## D-61 — Status kompetencji per roadmap item (N12)
Status: Adopted (2026-10-28, owner-confirmed on issue #7)

- Persist one mentor-assigned status per roadmap item: `NotAssessed`, `Learning`, `Practicing`, `Demonstrated`, or `NeedsRevisit`; new items default to `NotAssessed`.
- The system only suggests `Demonstrated` when all active required assignments are `Accepted`; it never changes the status. A mentor may set `Demonstrated` without an `Accepted` attempt only with a non-empty plain-text justification, limited to 1000 characters.
- Record each status change as an append-only history entry containing previous status, new status, mentor user ID, UTC timestamp, and any manual justification. The mentee sees the current status; attempt evidence and status history are mentor-only on the mentor roadmap.
- Competency progress is the percentage of active roadmap items whose status is `Demonstrated`; with no active items the percentage is zero. Keep the existing assignment-completion progress alongside it.
- Do not hard-delete a roadmap item after it has status history; deactivate it instead, preserving the append-only audit trail.

Impact: add `RoadmapItems.CompetencyStatus`, `CompetencyStatusHistory`, additive Roadmaps/Progress contract DTOs (including the `SuggestedDemonstrated` and evidence `AssignmentId` fields), and the `IRoadmapService.SetCompetencyStatusAsync` signature in the schema-change PR. Implement the method behavior, status authorization, evidence/history reads, suggestions, progress calculation, and UI only after the schema-change PR is merged.

## D-62 — Microsoft .NET 10.0.14 package updates
Status: Adopted (2026-10-28; maintenance update)

- Upgrade `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, and `Microsoft.AspNetCore.Mvc.Testing` from 10.0.13 to 10.0.14.
- This is a package maintenance update; no product behavior or new dependency is intended.

## D-63 — DecisionAnswer JSON v2 envelope (T04)
Status: Adopted (2026-10-28, confirmed by the user)

Context: `DecisionAnswerJson` v1 stores six architectural-answer fields at the document root. T04 needs a concrete v1-to-v2 migration pattern without changing the MVP answer required from mentees.

Decision:
- Keep the six fields and their meanings unchanged, and group them under an `answer` object in a document with `schemaVersion: 2`.
- Migrate v1 JSON by moving each existing value unchanged into `answer`; do not transform `null` values or documents with another version.
- New writes use v2; history reads both v1 and v2 into the existing mentee-facing six-field DTO.

Impact: the persisted JSON contract changes through a separate `schema-change` PR. No form fields or UI behavior change.

## D-64 — Serwer MCP w MentorPath.Web: agent jako kolejny klient serwisów aplikacyjnych
Status: Proposed (2026-10-29; do akceptacji w PR `schema-change` z kontraktem)

Kontekst: Mentor chce, aby agent AI (Claude Code, VS Code Copilot, własny agent w C#) czytał cele i postęp mentee, przeglądał katalog, szkicował ćwiczenia i składał roadmapę. Model Context Protocol (MCP) jest otwartym standardem takiego dostępu; oficjalny C# SDK (`ModelContextProtocol` 2.3.0, rewizja specyfikacji 2026-07-28: bezstanowy Streamable HTTP) jest utrzymywany wspólnie z Microsoftem. Serwer MCP jest jednocześnie demem na konferencję (12.11.2026) i narzędziem codziennej pracy mentora na instancji produkcyjnej (Azure App Service).

Decyzja:
- **Bez nowego projektu.** Serwer MCP to folder `src/MentorPath.Web/Mcp/` (Tools, Resources, Prompts, Actor, `McpResults`, `McpComposition`) w istniejącym hoście — tak jak `Endpoints/` i `Cli/`. Wdraża się z tym samym obrazem Docker i tym samym App Service. Nowe pakiety w `Directory.Packages.props`: `ModelContextProtocol.AspNetCore` 2.3.0 (serwer MCP) i `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14 (tokeny dla agentów; wersja zgodna z D-62).
- **Narzędzia wołają wyłącznie interfejsy z `Contracts/`** z `actorUserId` — autoryzacja i izolacja workspace'u pozostają w serwisach (D-01, D-08). Brak dostępu = `NotFound`, tak jak w UI. Narzędzia nie używają `AppDbContext`.
- **Lista narzędzi jest powierzchnią uprawnień agenta.** Odczyty: mentees, profil, dashboard, roadmapa, postęp, tematy, ćwiczenia. Zapisy: `create_topic`, `create_exercise_draft`, `create_roadmap`, `add_roadmap_topic`, `assign_exercise`, `reorder_roadmap_topics`. **Nie są wystawione**: publikacja ćwiczeń i roadmap, feedback, zaproszenia, notatki prywatne, operacje po stronie mentee. Zasoby: `mentorpath://mentees/{mentorshipId}/profile`, `mentorpath://catalog`. Prompty: `plan_roadmap`, `draft_exercise`.
- **Szkice tworzone przez agenta mają `ExerciseSource.AI`** (D-03 obowiązuje: zawsze `Draft`, publikuje człowiek). Wymaga to wystawienia w `IExerciseAuthoringService` istniejącego przeciążenia `CreateDraftAsync(actorUserId, request, ExerciseSource, ct)` — zmiana `Contracts/`, osobny PR `schema-change`, bez migracji.
- **Transport HTTP (produkcja):** `app.MapMcp("/mcp").RequireAuthorization(...)` z bearer JWT (HS256, klucz `Mcp:SigningKey` ≥ 32 bajty, `sub` = `AppUser.Id`, `iss`/`aud` = `mentorpath-mcp`, domyślna ważność 30 dni). Token wydaje komenda CLI `mcp-token --email <e-mail> [--days N]` w tym samym binarium (jak `create-mentor`); na produkcji uruchamiana w kontenerze App Service. Ścieżka `/mcp` jest wyłączona z re-execution stron statusu (jak `/api`). Docelowo: OAuth 2.1 z zewnętrznym serwerem autoryzacji — poza zakresem.
- **Transport stdio (lokalnie):** komenda CLI `mcp-stdio --actor <e-mail>` uruchamia ten sam zestaw narzędzi w procesie potomnym klienta (Claude Code, VS Code) na lokalnej bazie; dozwolona wyłącznie w `Development`. Cały logging idzie na stderr (stdout jest kanałem JSON-RPC).
- Serwer jest bezstanowy (domyślny tryb SDK); bez samplingu, roots i logowania MCP (zdeprecjonowane w 2026-07-28).

Wpływ: dwa pakiety i folder `Mcp/` w `MentorPath.Web`; dwie komendy CLI (`mcp-stdio`, `mcp-token`); zmienna `Mcp__SigningKey` w konfiguracji App Service (`DEPLOYMENT.md` §2); testy: izolacja każdego narzędzia (obcy mentor → `NotFound`) oraz test konwencji, że narzędzia nie używają `AppDbContext`/`IDbContextFactory`; `ARCHITECTURE.md` §2, §5 i §9, `README.md`. Pliki konfiguracji klientów w repo: `.mcp.json` (Claude Code), `.vscode/mcp.json` (VS Code).
