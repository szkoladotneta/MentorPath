# MentorPath — Domain Rules

Status: **żywy dokument** · zastępuje `docs/archive/AGENT_CONTRACTS.md` · ostatnia weryfikacja względem kodu: 2026-10-19.

Kolejność ważności: `MVP_SPEC.md` > `DECISIONS.md` > **ten dokument** > `ARCHITECTURE.md`.

Ten dokument zawiera **reguły i intencje**, których kod sam nie wyraża. **Kształty** (encje, DTO, sygnatury serwisów) są w kodzie i tam są autorytatywne:

- encje: `src/MentorPath.Data/Entities/`, konfiguracje i ograniczenia: `src/MentorPath.Data/Configurations/`, enumy: `src/MentorPath.Data/Enums/`,
- DTO i interfejsy serwisów: `src/MentorPath.Application/<Module>/Contracts/`,
- `Result`, `AppError`, `ErrorKind`, `Errors`, `InputLimits`: `src/MentorPath.Application/Common/`.

Reguły, które da się sprawdzić mechanicznie, pilnuje `tests/MentorPath.IntegrationTests/Isolation/ConventionTests.cs` i `ArchitectureTests.cs`. Jeśli kod i ten dokument się rozjeżdżają, napraw to w tym samym PR.

---

## 1. Konwencje

- Namespace: `MentorPath.Data.*`, `MentorPath.Application.<Module>[.Contracts]`.
- DTO = `public sealed record`. Encje nie występują w publicznych sygnaturach serwisów.
- Publiczna metoda serwisu: `(Guid actorUserId, <Request/Id...>, CancellationToken ct)` → `Task<Result>` / `Task<Result<T>>`. Wyjątki (jawnie wymienione w `ConventionTests`): anonimowe metody zaproszeń (`PreviewAsync`, `AcceptWithNewAccountAsync`) `IMentorProvisioningService.ProvisionMentorAsync` (CLI) oraz `IPersonalDataService.ExportAsync` / `AnonymizeAsync` (CLI, operator; D-42).
- Implementacje `internal sealed`; rejestracja w `<Module>Module.cs`.
- Teksty Markdown: suffix `Markdown`. Czas: `DateTime` UTC, suffix `Utc`, wyłącznie z `TimeProvider`.
- Komunikaty błędów dla UI po polsku; `AppError.Code` stabilny, angielski, `snake_case` z prefiksem modułu (np. `submission.already_completed`).
- Brak uprawnień ZAWSZE mapuje się na `ErrorKind.NotFound` (`<module>.not_found`).

## 2. Autoryzacja zasobowa

Jedyny punkt decyzji o dostępie: `IWorkspaceAccessService` (`src/MentorPath.Application/Common/Access`).

| Zasób | WorkspaceOwner | Mentee |
|---|---|---|
| Workspace | `Workspace.OwnerUserId == userId` | brak |
| Mentorship | workspace mentorshipu należy do usera | `MenteeUserId == userId` **i** `Status != Closed` |
| Assignment | workspace assignmentu należy do usera | mentorship dostępny (j.w.) **i** `Roadmap.Status == Published` |
| Submission | workspace submission należy do usera | `Submission.MenteeUserId == userId` **i** mentorship ≠ Closed |

- Rola Identity `PlatformOperator` nie daje dostępu do żadnego zasobu w aplikacji.
- `null`/`false` = brak dostępu **lub** brak zasobu (nierozróżnialne dla wywołującego).
- `WorkspaceId` jest zawsze wyliczany z aktora lub zasobu (D-08), nigdy z requestu.
- Mentee: `AssignmentAccess` zwracany też dla nieaktywnych assignmentów/itemów (dostęp do historii); flagi decydują o widoczności na roadmapie i o możliwości wysłania.
- Brak EF global query filters (D-22). Autoryzacja jest jawna w każdym serwisie.

## 3. Ograniczenia bazy

Źródło: `src/MentorPath.Data/Configurations/`. Poniżej reguły, na których opiera się logika serwisów — zmiana któregokolwiek wymaga PR `schema-change` i aktualizacji tej tabeli.

| Tabela | Ograniczenie |
|---|---|
| Workspaces | unique `OwnerUserId` |
| Mentorships | unique `(WorkspaceId, MenteeUserId)`; AK `(WorkspaceId, Id)` |
| Invitations | unique `TokenHash`; index `(WorkspaceId, NormalizedEmail)` |
| Topics | unique `(WorkspaceId, Slug)`; AK `(WorkspaceId, Id)`; nullable `ParentTopicId` — FK `(WorkspaceId, ParentTopicId)` → Topics AK (Restrict); check `ParentTopicId <> Id`; index `(WorkspaceId, ParentTopicId)`; cykle i głębokość ≤ 3 pilnuje serwis (D-45) |
| Exercises | FK `(WorkspaceId, TopicId)` → Topics AK; AK `(WorkspaceId, Id)` |
| QuizOptions | FK `ExerciseId` Cascade; partial unique `(ExerciseId) WHERE "IsCorrect"` |
| Roadmaps | unique `MentorshipId`; FK `(WorkspaceId, MentorshipId)` |
| RoadmapItems | unique `(RoadmapId, TopicId)`; FK `(WorkspaceId, RoadmapId)`, `(WorkspaceId, TopicId)` |
| Assignments | unique `(RoadmapItemId, ExerciseId)`; FK `(WorkspaceId, RoadmapItemId)`, `(WorkspaceId, ExerciseId)` |
| Submissions | unique `(AssignmentId, AttemptNumber)`; unique `(MenteeUserId, ClientRequestId)`; **partial unique `(AssignmentId) WHERE "Status" = 'PendingReview'`**; CHECK `ConfidenceBefore BETWEEN 1 AND 5`, `AttemptNumber > 0` |
| SubmissionAttachments | unique `SubmissionId`, unique `ObjectKey` |
| MentorFeedback | unique `SubmissionId`; AK `(WorkspaceId, Id)` |
| RubricCriteria | unique `(WorkspaceId, Name)`; AK `(WorkspaceId, Id)`; CHECK `Position >= 0` |
| ExerciseCriteria | PK `(ExerciseId, CriterionId)`; FK `(WorkspaceId, ExerciseId)` → Exercises AK Cascade; FK `(WorkspaceId, CriterionId)` → RubricCriteria AK |
| FeedbackCriterionScores | PK `(FeedbackId, CriterionId)`; FK `(WorkspaceId, FeedbackId)` → MentorFeedback AK; FK `(WorkspaceId, CriterionId)` → RubricCriteria AK; CHECK `Score BETWEEN 1 AND 5` |
| SubmissionReflections | unique `SubmissionId`; CHECK `ConfidenceAfter BETWEEN 1 AND 5` |
| AiGenerationRequests | FK `ExerciseId` SetNull |

- Pozostałe FK: **Restrict**. Żadnych kaskad na Submissions/Feedback/Reflections/Attachments.
- Indeks po `WorkspaceId` na każdej tabeli `IWorkspaceScoped`.
- Dozwolone twarde usuwanie: `Exercise` w statusie `Draft` (+ QuizOptions kaskadowo), `RoadmapItem`/`Assignment` bez submissions, oraz `SubmissionAttachments` wyłącznie przy anonimizacji użytkownika (D-42).
- Enumy zapisywane jako string. Role Identity: `Mentor`, `Mentee`, `PlatformOperator`.

## 4. `DecisionAnswerJson` (schemat v1)

```json
{ "schemaVersion": 1, "decision": "", "rationale": "", "alternatives": "",
  "tradeOffs": "", "risks": "", "reconsiderationConditions": "" }
```

Serializacja `System.Text.Json`, camelCase. Nowa wersja schematu = decyzja w `DECISIONS.md` + PR `schema-change`.

## 5. Własność zapisu (per moduł)

Moduł zapisuje tylko do tabel, które posiada. Odczyt dowolnej tabeli jest dozwolony po sprawdzeniu dostępu. Egzekwuje `ConventionTests.Modules_write_only_to_tables_they_own` — słownik `WriteOwnership` w teście musi odpowiadać tej tabeli.

| Moduł | Może zapisywać do |
|---|---|
| Workspaces | Workspaces |
| Mentorships | Mentorships |
| Invitations | Invitations, Mentorships |
| Catalog | Topics, Exercises, QuizOptions, RubricCriteria, ExerciseCriteria |
| AiAuthoring | AiGenerationRequests |
| Roadmaps | Roadmaps, RoadmapItems, Assignments |
| Submissions | Submissions (insert), SubmissionAttachments |
| Reviews | MentorFeedback, FeedbackCriterionScores, SubmissionReflections, Submissions (tylko `PendingReview → Accepted/NeedsRevision`) |
| Progress | nic (wszystko wyliczane) |
| Privacy | Mentorships (czyszczenie PII, `Status = Closed`), Submissions (czyszczenie PII), SubmissionAttachments (usuwanie), MentorFeedback, FeedbackCriterionScores i SubmissionReflections (czyszczenie tekstów), Invitations (czyszczenie e-maila) — wyłącznie anonimizacja z CLI (D-42) |

Tabele Identity (AspNetUsers/Roles) obsługują Identity i moduły Invitations/CLI przy tworzeniu kont oraz moduł Privacy przy anonimizacji (AspNetUsers, AspNetUserRoles, AspNetUserLogins, AspNetUserClaims, AspNetUserTokens — wyłącznie przez `UserManager`, D-42).

Serwisy bez aktora (`IMentorProvisioningService`, `IPersonalDataService`) mogą być używane wyłącznie przez komendy CLI (`src/MentorPath.Web/Cli`); egzekwuje `ConventionTests.Operator_services_are_used_only_by_the_CLI`.

## 6. Postęp (D-16)

Czysta funkcja `ProgressCalculator` jest jedynym miejscem reguł postępu.

- Assignment `Completed` ⇔ istnieje próba `Correct` lub `Accepted`.
- Aktywny temat `Completed` ⇔ wszystkie aktywne wymagane przypisania `Completed`. Temat bez aktywnych wymaganych przypisań: `CountsTowardsProgress = false`.
- `PercentComplete = CountedTopics == 0 ? 0 : floor(100 * Completed / Counted)`.
- Nic nie jest przechowywane. Ostatnia aktywność: 20 zdarzeń z 30 dni, wyliczane z Submissions/MentorFeedback/SubmissionReflections.
- Refleksja: tylko autor-mentee, tylko dla statusów `Correct/Incorrect/Accepted/NeedsRevision`, jednorazowa (D-15).

## 7. Maszyna stanów

```
Exercise:   Draft ──Publish──▶ Published ──Archive──▶ Archived
            Draft ──Delete (hard)        Published ──Clone──▶ nowy Draft
Roadmap:    Draft ──Publish──▶ Published   (brak cofnięcia publikacji)
Submission: Quiz:     insert → Correct | Incorrect
            AD / PT:  insert → PendingReview ──Feedback──▶ Accepted | NeedsRevision
Mentorship: Active ⇄ Paused, Active|Paused → Closed, Closed → Active (mentor)   (D-12)
```

Wysłanie nowej próby jest dozwolone ⇔ mentee jest autorem relacji **i** `Mentorship.Status == Active` **i** roadmapa `Published` **i** item + assignment aktywne **i** typ ćwiczenia zgodny z metodą **i** brak próby `Correct`/`Accepted` **i** brak próby `PendingReview`. `DueAtUtc` nie blokuje (informacyjny).

## 8. Współbieżność i idempotencja (wzorce obowiązkowe)

| Operacja | Mechanizm |
|---|---|
| Submit | Transakcja: `AttemptNumber = max + 1`; insert. Naruszenie `(MenteeUserId, ClientRequestId)` → zwróć istniejącą próbę z `WasDuplicate = true` (jeśli inny AssignmentId → `submission.client_request_reused`). Naruszenie `(AssignmentId, AttemptNumber)` lub partial-unique PendingReview → `submission.concurrent_attempt` (Conflict). Upload: przy duplikacie usuń właśnie zapisany obiekt. |
| Feedback | Jedna transakcja: `ExecuteUpdate Submissions SET Status=@verdict WHERE Id=@id AND WorkspaceId=@ws AND Status='PendingReview'` → 0 wierszy = Conflict; insert `MentorFeedback` (unique SubmissionId). |
| Akceptacja zaproszenia | `ExecuteUpdate Invitations SET AcceptedAtUtc=now WHERE Id=@id AND AcceptedAtUtc IS NULL AND ExpiresAtUtc > now` → 0 = `invitation.invalid_or_used`; insert Mentorship (unique). Jedna transakcja z utworzeniem konta. |
| Publikacja ćwiczenia | `ExecuteUpdate ... WHERE Status='Draft'`. |
| Limit AI | count-then-insert; dopuszczalne przekroczenie o 1 przy wyścigu (tylko mentor) — znane ryzyko. |

## 9. Scenariusz wypuszczenia MVP

Cały scenariusz musi przechodzić jako test `tests/MentorPath.IntegrationTests/Release/ReleaseScenarioTests.cs`:

1. Operator tworzy Mentor A i Mentor B.
2. Mentor A zaprasza mentee.
3. Mentor A generuje draft ćwiczenia przez AI.
4. Mentor edytuje i publikuje ćwiczenie.
5. Mentor tworzy roadmapę.
6. Mentee rozwiązuje quiz.
7. Mentee przesyła ADR lub repozytorium.
8. Mentor wystawia NeedsRevision.
9. Mentee poprawia rozwiązanie.
10. Mentor wystawia Accepted.
11. Postęp roadmapy się aktualizuje.
12. Mentor B nie może odczytać żadnego zasobu z tego procesu.

Rozwój po MVP: `docs/BACKLOG_NEXT.md`.
