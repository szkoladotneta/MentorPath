> **ARCHIWUM (2026-10-19, D-41).** Dokument historyczny — nie jest źródłem wymagań ani reguł. Aktualne: `docs/MVP_SPEC.md`, `docs/DECISIONS.md`, `docs/DOMAIN_RULES.md`, `docs/BACKLOG_NEXT.md`.

# MentorPath — Agent Contracts (baseline)

Status: **ADOPTED (M0, 2026-10-17)** — zmiany tylko przez Change Request.

Kolejność ważności: `MVP_SPEC.md` > `DECISIONS.md` (Approved) > **ten dokument** > `ARCHITECTURE.md` > `.github/agents/*.agent.md`.

Zmiana czegokolwiek w §3–§8 = Change Request (`AGENTS.md` §5).

---

## 1. Konwencje

- Namespace: `MentorPath.Data.*`, `MentorPath.Application.<Module>[.Contracts]`.
- DTO = `public sealed record`. Brak encji w sygnaturach publicznych serwisów.
- Każda publiczna metoda serwisu: `(Guid actorUserId, <Request/Id...>, CancellationToken ct)` → `Task<Result>` / `Task<Result<T>>`. Wyjątek: `IWorkspaceAccessService` (zwraca `bool`/access record) oraz metody anonimowe zaproszeń.
- Implementacje `internal sealed`; rejestracja w `<Module>Module.cs` (właściciel modułu).
- Teksty Markdown: suffix `Markdown`. Czas: `DateTime` UTC, suffix `Utc`.
- Komunikaty błędów dla UI po polsku; `AppError.Code` stabilny, angielski, `snake_case` z prefiksem modułu.

---

## 2. Common (A1) — `MentorPath.Application.Common`

```csharp
public enum ErrorKind { NotFound, Validation, Conflict, QuotaExceeded, Unavailable }

public sealed record AppError(
    ErrorKind Kind,
    string Code,                                   // np. "submission.pending_review_exists"
    string Message,                                // PL, do wyświetlenia
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

public class Result
{
    public bool IsSuccess { get; }
    public AppError? Error { get; }
    public static Result Success();
    public static Result Fail(AppError error);
}
public sealed class Result<T> : Result
{
    public T? Value { get; }
    public static Result<T> Success(T value);
    public static new Result<T> Fail(AppError error);
}
// Brak uprawnień ZAWSZE mapowany na ErrorKind.NotFound (code: "<module>.not_found").
```

### 2.1. Autoryzacja zasobowa

```csharp
public enum AccessRole { WorkspaceOwner, Mentee }

public sealed record MentorshipAccess(
    Guid MentorshipId, Guid WorkspaceId, Guid MenteeUserId,
    MentorshipStatus Status, AccessRole Role)
{
    public bool MenteeCanWrite => Role == AccessRole.Mentee && Status == MentorshipStatus.Active;
}

public sealed record AssignmentAccess(
    Guid AssignmentId, Guid WorkspaceId, Guid MentorshipId, Guid MenteeUserId,
    Guid ExerciseId, AccessRole Role,
    MentorshipStatus MentorshipStatus, RoadmapStatus RoadmapStatus,
    bool AssignmentIsActive, bool RoadmapItemIsActive);

public interface IWorkspaceAccessService
{
    Task<Guid?> GetOwnedWorkspaceIdAsync(Guid userId, CancellationToken ct);
    Task<bool> CanManageWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct);
    Task<MentorshipAccess?> GetMentorshipAccessAsync(Guid userId, Guid mentorshipId, CancellationToken ct);
    Task<bool> CanAccessMentorshipAsync(Guid userId, Guid mentorshipId, CancellationToken ct);
    Task<AssignmentAccess?> GetAssignmentAccessAsync(Guid userId, Guid assignmentId, CancellationToken ct);
    Task<bool> CanAccessSubmissionAsync(Guid userId, Guid submissionId, CancellationToken ct);
}
```

Reguły (jedyna implementacja: A1, testy: A1 + A5):

| Zasób | WorkspaceOwner | Mentee |
|---|---|---|
| Workspace | `Workspace.OwnerUserId == userId` | brak |
| Mentorship | workspace mentorshipu należy do usera | `MenteeUserId == userId` **i** `Status != Closed` |
| Assignment | workspace assignmentu należy do usera | mentorship dostępny (j.w.) **i** `Roadmap.Status == Published` |
| Submission | workspace submission należy do usera | `Submission.MenteeUserId == userId` **i** mentorship ≠ Closed |

- Rola `PlatformOperator` nie daje dostępu do żadnego zasobu w aplikacji.
- `null`/`false` = brak dostępu **lub** brak zasobu (nierozróżnialne).
- Mentee: `AssignmentAccess` zwracany też dla nieaktywnych assignmentów/itemów (dostęp do historii); flagi decydują o widoczności na roadmapie i o możliwości wysłania.

### 2.2. Pozostałe kontrakty Common

```csharp
public interface IFileStorage
{
    Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct);   // FileNotFoundException gdy brak
    Task DeleteAsync(string objectKey, CancellationToken ct);              // idempotentne
}
// Implementacje: S3FileStorage (prod/dev, dowolny S3-compatible; lokalnie SeaweedFS), InMemoryFileStorage (testy).

public interface IMarkdownRenderer
{
    string RenderToSafeHtml(string? markdown);   // Markdig DisableHtml + HtmlSanitizer
}
// UI: wyłącznie komponent <Markdown Value="..." /> (A1).

public static class InputLimits
{
    public const int Title = 200, Slug = 150, Category = 80, ShortText = 1000,
                     Markdown = 20_000, ExternalUrl = 2000, DisplayName = 200, Email = 320;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
}
```

Czas: `TimeProvider` (wbudowany). E-mail: `IEmailSender<AppUser>` z ASP.NET Core Identity (SMTP; tylko reset hasła — D-11).

---

## 3. Model encji (A1 — `MentorPath.Data`) — ZAMROŻONY

Zgodny z `MVP_SPEC.md` §5 + delty z **D-10** (oznaczone `// Δ`).

```csharp
public interface IWorkspaceScoped { Guid WorkspaceId { get; } }

public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";          // Δ varchar(200)
    public DateTime CreatedAtUtc { get; set; }             // Δ
}

public sealed class Workspace          { Guid Id; Guid OwnerUserId /*unique*/; string Name; DateTime CreatedAtUtc; }

public sealed class Mentorship : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid MenteeUserId; string? Goal; ExperienceLevel? CurrentLevel; int? WeeklyHours;
  string? Challenges; string? MentorPrivateNotes; MentorshipStatus Status; DateTime CreatedAtUtc; DateTime UpdatedAtUtc; }

public sealed class Invitation : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid InvitedByUserId; string NormalizedEmail; string TokenHash /*sha256 hex, unique*/;
  DateTime ExpiresAtUtc; DateTime? AcceptedAtUtc; DateTime CreatedAtUtc; }

public sealed class Topic : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; string Slug; string Title; string Category; Difficulty Difficulty;
  string DescriptionMarkdown; string LearningObjectivesMarkdown; bool IsArchived; DateTime CreatedAtUtc; }

public sealed class Exercise : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid TopicId; ExerciseType Type; ExerciseStatus Status; ExerciseSource Source;
  string Title;
  string ScenarioMarkdown;               // Δ wymagane dla wszystkich typów
  string InstructionsMarkdown;           // ograniczenia/wymagania ("" gdy brak)
  string? ExpectedOutcomeMarkdown;       // Δ AD: oczekiwane rezultaty; PT: oczekiwane artefakty
  string RubricMarkdown;                 // kryteria oceny ("" gdy brak)
  string? DiscussionQuestionsMarkdown;   // Δ PT: pytania o decyzje implementacyjne; AI: pytania pogłębiające
  string MentorGuideMarkdown;            // PRYWATNE ("" gdy brak)
  string? ExplanationMarkdown;           // quiz
  int? EstimatedMinutes; DateTime? PublishedAtUtc; DateTime CreatedAtUtc;
  List<QuizOption> QuizOptions; }

public sealed class QuizOption
{ Guid Id; Guid ExerciseId; string Text; bool IsCorrect; string ExplanationMarkdown; int Position; }

public sealed class Roadmap : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid MentorshipId /*unique*/; string Title; string ObjectiveMarkdown;
  RoadmapStatus Status; DateTime? PublishedAtUtc; DateTime CreatedAtUtc; }

public sealed class RoadmapItem : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId /*Δ*/; Guid RoadmapId; Guid TopicId; int Position; string? MentorNoteMarkdown; bool IsActive; }

public sealed class Assignment : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid RoadmapItemId; Guid ExerciseId; int Position; bool IsRequired; bool IsActive; DateTime? DueAtUtc; }

public sealed class Submission : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid AssignmentId; Guid MenteeUserId; Guid ClientRequestId; int AttemptNumber;
  SubmissionStatus Status; Guid? SelectedQuizOptionId; string? DecisionAnswerJson /*jsonb*/;
  string? AnswerMarkdown; string? ExternalUrl; short ConfidenceBefore; DateTime SubmittedAtUtc; }

public sealed class SubmissionAttachment : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid SubmissionId /*unique*/; string ObjectKey /*unique*/; string OriginalFileName;
  string ContentType; long SizeBytes; DateTime CreatedAtUtc; }

public sealed class MentorFeedback : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid SubmissionId /*unique*/; Guid MentorUserId; ReviewVerdict Verdict;
  string StrengthsMarkdown; string ImprovementsMarkdown; string ChallengeQuestionMarkdown; DateTime CreatedAtUtc; }

public sealed class SubmissionReflection
{ Guid Id; Guid SubmissionId /*unique*/; short ConfidenceAfter; string TakeawayMarkdown; DateTime CreatedAtUtc; }

public sealed class AiGenerationRequest : IWorkspaceScoped
{ Guid Id; Guid WorkspaceId; Guid RequestedByUserId; Guid? ExerciseId; ExerciseType ExerciseType; AiRequestStatus Status;
  string Provider; string Model; string PromptTemplateVersion; int? InputTokens; int? OutputTokens;
  string? ErrorCode; DateTime CreatedAtUtc; DateTime? CompletedAtUtc; }
```
(Skrót notacji: pola to `public` auto-properties `{ get; set; }`.)

### 3.1. Enumy (zapisywane jako string)

```csharp
public enum ExerciseType      { QuizSingleChoice, ArchitecturalDecision, PracticalTask }
public enum ExerciseStatus    { Draft, Published, Archived }
public enum ExerciseSource    { Manual, AI }
public enum RoadmapStatus     { Draft, Published }
public enum SubmissionStatus  { Correct, Incorrect, PendingReview, Accepted, NeedsRevision }
public enum MentorshipStatus  { Active, Paused, Closed }
public enum AiRequestStatus   { Pending, Completed, Failed }
public enum ReviewVerdict     { Accepted, NeedsRevision }          // brak "Rejected"
public enum Difficulty        { Beginner, Intermediate, Advanced } // Topic + wejście AI
public enum ExperienceLevel   { Junior, Mid, Senior, Lead }        // onboarding
```
Role Identity: `Mentor`, `Mentee`, `PlatformOperator`.

### 3.2. Ograniczenia bazy (A1 w `InitialSchema`)

| Tabela | Ograniczenie |
|---|---|
| Workspaces | unique `OwnerUserId` |
| Mentorships | unique `(WorkspaceId, MenteeUserId)`; AK `(WorkspaceId, Id)` |
| Invitations | unique `TokenHash`; index `(WorkspaceId, NormalizedEmail)` |
| Topics | unique `(WorkspaceId, Slug)`; AK `(WorkspaceId, Id)` |
| Exercises | FK `(WorkspaceId, TopicId)` → Topics AK; AK `(WorkspaceId, Id)`; index `(WorkspaceId, TopicId, Status)` |
| QuizOptions | FK `ExerciseId` **Cascade**; partial unique `(ExerciseId) WHERE "IsCorrect"` |
| Roadmaps | unique `MentorshipId`; FK `(WorkspaceId, MentorshipId)` → Mentorships AK; AK `(WorkspaceId, Id)` |
| RoadmapItems | unique `(RoadmapId, TopicId)`; FK `(WorkspaceId, RoadmapId)`, `(WorkspaceId, TopicId)`; AK `(WorkspaceId, Id)` |
| Assignments | unique `(RoadmapItemId, ExerciseId)`; FK `(WorkspaceId, RoadmapItemId)`, `(WorkspaceId, ExerciseId)`; AK `(WorkspaceId, Id)` |
| Submissions | unique `(AssignmentId, AttemptNumber)`; unique `(MenteeUserId, ClientRequestId)`; **partial unique `(AssignmentId) WHERE "Status" = 'PendingReview'`**; FK `(WorkspaceId, AssignmentId)`; AK `(WorkspaceId, Id)`; index `(WorkspaceId, Status, SubmittedAtUtc)`; CHECK `ConfidenceBefore BETWEEN 1 AND 5`, `AttemptNumber > 0` |
| SubmissionAttachments | unique `SubmissionId`, unique `ObjectKey`; FK `(WorkspaceId, SubmissionId)` |
| MentorFeedback | unique `SubmissionId`; FK `(WorkspaceId, SubmissionId)` |
| SubmissionReflections | unique `SubmissionId`; CHECK `ConfidenceAfter BETWEEN 1 AND 5` |
| AiGenerationRequests | FK `ExerciseId` **SetNull**; index `(WorkspaceId, CreatedAtUtc)` |

- Wszystkie pozostałe FK: **Restrict**. Żadnych kaskad na Submissions/Feedback/Reflections/Attachments.
- Indeks po `WorkspaceId` na każdej tabeli `IWorkspaceScoped`.
- Dozwolone twarde usuwanie w MVP: tylko `Exercise` w statusie `Draft` (+ QuizOptions kaskadowo), `RoadmapItem`/`Assignment` bez submissions.

### 3.3. `DecisionAnswerJson` (schemat v1)

```json
{ "schemaVersion": 1, "decision": "", "rationale": "", "alternatives": "",
  "tradeOffs": "", "risks": "", "reconsiderationConditions": "" }
```
Serializacja `System.Text.Json`, camelCase. Nowa wersja schematu = CR.

---

## 4. Macierz własności zapisu

| Tabela | Insert/Update/Delete | Uwagi |
|---|---|---|
| AspNetUsers/Roles, Workspaces, Mentorships, Invitations | **A1** | |
| Topics, Exercises, QuizOptions, AiGenerationRequests | **A2** | |
| Roadmaps, RoadmapItems, Assignments | **A3** | |
| Submissions (insert), SubmissionAttachments | **A3** | |
| Submissions.Status: `PendingReview → Accepted/NeedsRevision` | **A4** | jedyny zapis A4 do Submissions |
| MentorFeedback, SubmissionReflections | **A4** | |

Odczyt dowolnej tabeli dozwolony po sprawdzeniu dostępu (ARCHITECTURE §3).

---

## 5. Kontrakty modułów

### 5.1. Workspaces / Mentorships / Invitations (A1)

```csharp
public interface IMentorProvisioningService          // używane przez CLI create-mentor
{
    Task<Result<ProvisionMentorResult>> ProvisionMentorAsync(ProvisionMentorRequest request, CancellationToken ct);
}
public sealed record ProvisionMentorRequest(string Email, string DisplayName, string WorkspaceName);
public sealed record ProvisionMentorResult(Guid UserId, Guid WorkspaceId, string PasswordSetupUrl);

public interface IInvitationService
{
    Task<Result<CreateInvitationResult>> CreateAsync(Guid actorUserId, CreateInvitationRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<InvitationListItem>>> ListAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<InvitationPreview>> PreviewAsync(string token, CancellationToken ct);                       // anonimowe
    Task<Result<AcceptInvitationResult>> AcceptWithNewAccountAsync(AcceptWithNewAccountRequest request, CancellationToken ct); // anonimowe
    Task<Result<AcceptInvitationResult>> AcceptAsExistingUserAsync(Guid actorUserId, string token, CancellationToken ct);
}
public sealed record CreateInvitationRequest(string Email);
public sealed record CreateInvitationResult(Guid InvitationId, string AcceptUrl, DateTime ExpiresAtUtc); // surowy token tylko tutaj
public enum InvitationState { Pending, Accepted, Expired }
public sealed record InvitationListItem(Guid InvitationId, string Email, InvitationState State, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
public sealed record InvitationPreview(string WorkspaceName, string MentorDisplayName, string MaskedEmail, bool AccountExists);
public sealed record AcceptWithNewAccountRequest(string Token, string DisplayName, string Password);
public sealed record AcceptInvitationResult(Guid MentorshipId, Guid UserId);

public interface IMentorshipService
{
    // mentee
    Task<Result<IReadOnlyList<MenteeMentorshipListItem>>> ListForMenteeAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<OnboardingDto>> GetOnboardingAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result> SaveOnboardingAsync(Guid actorUserId, Guid mentorshipId, SaveOnboardingRequest request, CancellationToken ct);
    // mentor
    Task<Result<IReadOnlyList<MentorMenteeListItem>>> ListForMentorAsync(Guid actorUserId, CancellationToken ct);
    Task<Result<MentorMenteeDetail>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
    Task<Result> UpdatePrivateNotesAsync(Guid actorUserId, Guid mentorshipId, string? notesMarkdown, CancellationToken ct);
    Task<Result> ChangeStatusAsync(Guid actorUserId, Guid mentorshipId, MentorshipStatus status, CancellationToken ct);
}
public sealed record MenteeMentorshipListItem(Guid MentorshipId, string WorkspaceName, string MentorDisplayName, MentorshipStatus Status, bool OnboardingCompleted);
public sealed record OnboardingDto(Guid MentorshipId, string? Goal, ExperienceLevel? CurrentLevel, string? Challenges, int? WeeklyHours);
public sealed record SaveOnboardingRequest(string Goal, ExperienceLevel CurrentLevel, string Challenges, int WeeklyHours); // 1..80
public sealed record MentorMenteeListItem(Guid MentorshipId, string MenteeDisplayName, string MenteeEmail, MentorshipStatus Status, bool OnboardingCompleted, DateTime CreatedAtUtc);
public sealed record MentorMenteeDetail(Guid MentorshipId, string MenteeDisplayName, string MenteeEmail, MentorshipStatus Status,
    string? Goal, ExperienceLevel? CurrentLevel, string? Challenges, int? WeeklyHours, string? MentorPrivateNotesMarkdown, DateTime CreatedAtUtc);
```
Reguły: token 32 B CSPRNG, base64url; w DB `SHA-256` hex. Ważność 7 dni. Szczegóły e-mail — D-11; nowe zaproszenie dla tego samego e-maila w workspace wygasza poprzednie pending (D-27).

### 5.2. Catalog (A2)

```csharp
public interface ITopicService
{
    Task<Result<IReadOnlyList<TopicListItem>>> ListAsync(Guid actorUserId, bool includeArchived, CancellationToken ct);
    Task<Result<TopicDetail>> GetAsync(Guid actorUserId, Guid topicId, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid actorUserId, UpsertTopicRequest request, CancellationToken ct);
    Task<Result> UpdateAsync(Guid actorUserId, Guid topicId, UpsertTopicRequest request, CancellationToken ct);
    Task<Result> SetArchivedAsync(Guid actorUserId, Guid topicId, bool isArchived, CancellationToken ct);
}
public sealed record UpsertTopicRequest(string Title, string Slug, string Category, Difficulty Difficulty,
    string DescriptionMarkdown, string LearningObjectivesMarkdown);
public sealed record TopicListItem(Guid TopicId, string Title, string Slug, string Category, Difficulty Difficulty, bool IsArchived, int ExerciseCount);
public sealed record TopicDetail(Guid TopicId, string Title, string Slug, string Category, Difficulty Difficulty,
    string DescriptionMarkdown, string LearningObjectivesMarkdown, bool IsArchived, IReadOnlyList<ExerciseListItem> Exercises);

public interface IExerciseAuthoringService
{
    Task<Result<Guid>> CreateDraftAsync(Guid actorUserId, CreateExerciseDraftRequest request, CancellationToken ct);
    Task<Result> UpdateDraftAsync(Guid actorUserId, Guid exerciseId, ExerciseContent content, CancellationToken ct);
    Task<Result> PublishAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result<Guid>> CloneAsDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result> ArchiveAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result> DeleteDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);         // "Odrzuć" (AI-03)
    Task<Result<MentorExerciseDto>> GetMentorExerciseAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result<IReadOnlyList<ExerciseListItem>>> ListAsync(Guid actorUserId, ExerciseListFilter filter, CancellationToken ct);
    Task<Result<MenteeExerciseDto>> GetMenteeExerciseAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
}
public sealed record CreateExerciseDraftRequest(Guid TopicId, ExerciseType Type, ExerciseContent Content);
public sealed record ExerciseContent(
    string Title, string ScenarioMarkdown, string? InstructionsMarkdown, string? ExpectedOutcomeMarkdown,
    string? RubricMarkdown, string? DiscussionQuestionsMarkdown, string? ExplanationMarkdown,
    string? MentorGuideMarkdown, int? EstimatedMinutes, IReadOnlyList<QuizOptionInput> QuizOptions);
public sealed record QuizOptionInput(string Text, bool IsCorrect, string? ExplanationMarkdown);
public sealed record ExerciseListFilter(Guid? TopicId, ExerciseStatus? Status, ExerciseType? Type);
public sealed record ExerciseListItem(Guid ExerciseId, Guid TopicId, string Title, ExerciseType Type, ExerciseStatus Status, ExerciseSource Source, DateTime CreatedAtUtc);
public sealed record MentorExerciseDto(Guid ExerciseId, Guid TopicId, string TopicTitle, ExerciseType Type, ExerciseStatus Status,
    ExerciseSource Source, ExerciseContent Content, DateTime? PublishedAtUtc, DateTime CreatedAtUtc);

// BEZPIECZNY DTO DLA MENTEE — typ z założenia NIE MA pól: IsCorrect, MentorGuideMarkdown, ExplanationMarkdown
public sealed record MenteeExerciseDto(Guid ExerciseId, ExerciseType Type, string Title, string ScenarioMarkdown,
    string? InstructionsMarkdown, string? ExpectedOutcomeMarkdown, string? RubricMarkdown,
    string? DiscussionQuestionsMarkdown, int? EstimatedMinutes, IReadOnlyList<MenteeQuizOptionDto> QuizOptions);
public sealed record MenteeQuizOptionDto(Guid QuizOptionId, string Text, int Position);
```
- Kryteria oceny (`RubricMarkdown`) są widoczne dla mentee (spec §7.6 "Kryteria"); `MentorGuideMarkdown` nigdy.
- `GetMenteeExerciseAsync` wymaga `GetAssignmentAccessAsync` z rolą `Mentee`.

Walidacja treści (D-13): zapis draftu = walidacja strukturalna; publikacja = pełna.

| Pole | Quiz | ArchitecturalDecision | PracticalTask |
|---|---|---|---|
| Title, Scenario | wymagane | wymagane | wymagane |
| Instructions | opcj. | **wymagane** (ograniczenia/wymagania) | **wymagane** (wymagania) |
| ExpectedOutcome | musi być puste | **wymagane** | **wymagane** (artefakty) |
| Rubric | opcj. | **wymagane** | **wymagane** |
| DiscussionQuestions | opcj. | opcj. | **wymagane** |
| Explanation | **wymagane** | puste | puste |
| QuizOptions | **2–6, dokładnie 1 IsCorrect**, tekst wymagany | puste | puste |
| EstimatedMinutes | 1–600 lub null | j.w. | j.w. |

Draft: tylko Title + limity długości + `≤ 1` poprawna opcja. Published: brak `UpdateDraftAsync` (`catalog.exercise_not_draft`). Temat zarchiwizowany: brak nowych ćwiczeń. Ćwiczenie `Archived`: nie można nowo przypisać; istniejące przypisania działają (D-26).

### 5.3. AI Authoring (A2)

```csharp
public interface IAiExerciseDraftService
{
    Task<Result<AiDraftResult>> GenerateDraftAsync(Guid actorUserId, GenerateExerciseDraftRequest request, CancellationToken ct);
    Task<Result<AiQuotaDto>> GetQuotaAsync(Guid actorUserId, CancellationToken ct);
}
public sealed record GenerateExerciseDraftRequest(Guid TopicId, ExerciseType Type, Difficulty Difficulty,
    string LearningObjectives, string? ContextAndConstraints, int? EstimatedMinutes);
public sealed record AiDraftResult(Guid ExerciseId, Guid AiRequestId);   // Exercise w statusie Draft, Source = AI
public sealed record AiQuotaDto(int Used, int Limit, DateTime PeriodEndsAtUtc);

// Port dostawcy (wewnętrzny dla A2)
public interface IExerciseDraftGenerator
{
    string ProviderName { get; }
    Task<DraftGenerationResponse> GenerateAsync(DraftGenerationPrompt prompt, CancellationToken ct);
}
public sealed record DraftGenerationPrompt(string PromptTemplateVersion, string SystemPrompt, string UserPrompt, string JsonSchema, int MaxOutputTokens);
public sealed record DraftGenerationResponse(bool Success, string? RawJson, string Model, int? InputTokens, int? OutputTokens, string? ErrorCode);
```
Schemat odpowiedzi AI v1 (`PromptTemplateVersion = "exercise-draft-v1"`):
```json
{ "title": "", "scenarioMarkdown": "", "instructionsMarkdown": "", "expectedOutcomeMarkdown": null,
  "rubricMarkdown": "", "mentorGuideMarkdown": "", "explanationMarkdown": null,
  "discussionQuestions": [""], "quizOptions": [ { "text": "", "isCorrect": false, "explanationMarkdown": "" } ] }
```
Błędy: `ai.quota_exceeded` (QuotaExceeded), `ai.provider_unavailable` (Unavailable), `ai.invalid_output` (Validation). Każde wywołanie → wiersz `AiGenerationRequests` (Pending → Completed/Failed). Pełna walidacja (publikacyjna) + brak HTML przed zapisem draftu.

### 5.4. Roadmaps (A3)

```csharp
public interface IRoadmapService
{
    // mentor
    Task<Result<Guid>> CreateAsync(Guid actorUserId, CreateRoadmapRequest request, CancellationToken ct);
    Task<Result<MentorRoadmapDto?>> GetForMentorAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct); // null = brak roadmapy
    Task<Result> UpdateDetailsAsync(Guid actorUserId, Guid roadmapId, UpdateRoadmapRequest request, CancellationToken ct);
    Task<Result<Guid>> AddTopicAsync(Guid actorUserId, AddRoadmapTopicRequest request, CancellationToken ct);
    Task<Result> UpdateTopicNoteAsync(Guid actorUserId, Guid roadmapItemId, string? mentorNoteMarkdown, CancellationToken ct);
    Task<Result> ReorderTopicsAsync(Guid actorUserId, Guid roadmapId, IReadOnlyList<Guid> orderedRoadmapItemIds, CancellationToken ct);
    Task<Result> SetTopicActiveAsync(Guid actorUserId, Guid roadmapItemId, bool isActive, CancellationToken ct);
    Task<Result> RemoveTopicAsync(Guid actorUserId, Guid roadmapItemId, CancellationToken ct);   // tylko bez submissions
    Task<Result<Guid>> AssignExerciseAsync(Guid actorUserId, AssignExerciseRequest request, CancellationToken ct);
    Task<Result> UpdateAssignmentAsync(Guid actorUserId, Guid assignmentId, UpdateAssignmentRequest request, CancellationToken ct);
    Task<Result> ReorderAssignmentsAsync(Guid actorUserId, Guid roadmapItemId, IReadOnlyList<Guid> orderedAssignmentIds, CancellationToken ct);
    Task<Result> SetAssignmentActiveAsync(Guid actorUserId, Guid assignmentId, bool isActive, CancellationToken ct);
    Task<Result> RemoveAssignmentAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct); // tylko bez submissions
    Task<Result> PublishAsync(Guid actorUserId, Guid roadmapId, CancellationToken ct);
    // mentee
    Task<Result<MenteeRoadmapDto>> GetForMenteeAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct);
}
public sealed record CreateRoadmapRequest(Guid MentorshipId, string Title, string ObjectiveMarkdown);
public sealed record UpdateRoadmapRequest(string Title, string ObjectiveMarkdown);
public sealed record AddRoadmapTopicRequest(Guid RoadmapId, Guid TopicId, string? MentorNoteMarkdown);
public sealed record AssignExerciseRequest(Guid RoadmapItemId, Guid ExerciseId, bool IsRequired, DateTime? DueAtUtc);
public sealed record UpdateAssignmentRequest(bool IsRequired, DateTime? DueAtUtc);

public sealed record MentorRoadmapDto(Guid RoadmapId, Guid MentorshipId, string Title, string ObjectiveMarkdown, RoadmapStatus Status,
    DateTime? PublishedAtUtc, IReadOnlyList<MentorRoadmapItemDto> Items);
public sealed record MentorRoadmapItemDto(Guid RoadmapItemId, Guid TopicId, string TopicTitle, int Position, bool IsActive,
    string? MentorNoteMarkdown, bool HasSubmissions, IReadOnlyList<MentorAssignmentDto> Assignments);
public sealed record MentorAssignmentDto(Guid AssignmentId, Guid ExerciseId, string ExerciseTitle, ExerciseType Type,
    int Position, bool IsRequired, bool IsActive, DateTime? DueAtUtc, int SubmissionCount);

public sealed record MenteeRoadmapDto(Guid RoadmapId, Guid MentorshipId, string Title, string ObjectiveMarkdown, IReadOnlyList<MenteeRoadmapItemDto> Items);
public sealed record MenteeRoadmapItemDto(Guid RoadmapItemId, string TopicTitle, string TopicDescriptionMarkdown,
    string? MentorNoteMarkdown, int Position, IReadOnlyList<MenteeAssignmentListItem> Assignments);
public sealed record MenteeAssignmentListItem(Guid AssignmentId, string ExerciseTitle, ExerciseType Type, bool IsRequired,
    DateTime? DueAtUtc, SubmissionStatus? LatestStatus, int AttemptCount);
```
Reguły: 1 roadmapa / mentorship; temat i ćwiczenie z tego samego workspace'u; `Exercise.TopicId == RoadmapItem.TopicId`; tylko `Exercise.Status == Published`; temat niezarchiwizowany przy dodawaniu. Publikacja: ≥ 1 aktywny temat z ≥ 1 aktywnym wymaganym przypisaniem. Mentee widzi tylko `Published` roadmapę i tylko aktywne itemy/przypisania, posortowane po `Position` (D-17). `Position` = 0..n-1, przenumerowane przy reorder.

### 5.5. Submissions (A3)

```csharp
public interface ISubmissionService
{
    Task<Result<MenteeAssignmentDto>> GetAssignmentForMenteeAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitQuizAsync(Guid actorUserId, SubmitQuizRequest request, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitDecisionAsync(Guid actorUserId, SubmitDecisionRequest request, CancellationToken ct);
    Task<Result<SubmissionResultDto>> SubmitPracticalAsync(Guid actorUserId, SubmitPracticalRequest request, CancellationToken ct);
    Task<Result<SubmissionHistoryDto>> GetHistoryAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct); // mentee lub mentor
    Task<Result<AttachmentDownload>> OpenAttachmentAsync(Guid actorUserId, Guid attachmentId, CancellationToken ct);
}
public sealed record DecisionAnswerV1(string Decision, string Rationale, string Alternatives, string TradeOffs,
    string Risks, string ReconsiderationConditions);
public sealed record SubmitQuizRequest(Guid AssignmentId, Guid ClientRequestId, Guid SelectedQuizOptionId, string RationaleMarkdown, int ConfidenceBefore);
public sealed record SubmitDecisionRequest(Guid AssignmentId, Guid ClientRequestId, DecisionAnswerV1 Answer, int ConfidenceBefore);
public sealed record SubmitPracticalRequest(Guid AssignmentId, Guid ClientRequestId, string RationaleMarkdown, int ConfidenceBefore,
    string? ExternalUrl, FileUpload? File);                       // co najmniej jedno z ExternalUrl / File (D-25)
public sealed record FileUpload(string FileName, string ContentType, long SizeBytes, Stream Content);

public enum SubmitBlockReason { None, MentorshipNotActive, AssignmentInactive, PendingReview, AlreadyCompleted }
public sealed record MenteeAssignmentDto(Guid AssignmentId, Guid MentorshipId, string TopicTitle, bool IsRequired, DateTime? DueAtUtc,
    MenteeExerciseDto Exercise, SubmitBlockReason SubmitBlockReason, int AttemptCount);
public sealed record SubmissionResultDto(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status, bool WasDuplicate, QuizOutcomeDto? Quiz);
public sealed record QuizOutcomeDto(bool IsCorrect, string SelectedOptionExplanationMarkdown, string? ExerciseExplanationMarkdown); // ostatnie tylko gdy IsCorrect

public sealed record SubmissionHistoryDto(Guid AssignmentId, string ExerciseTitle, ExerciseType Type, IReadOnlyList<SubmissionHistoryItem> Attempts); // malejąco po AttemptNumber
public sealed record SubmissionHistoryItem(Guid SubmissionId, int AttemptNumber, SubmissionStatus Status, DateTime SubmittedAtUtc,
    int ConfidenceBefore, string? SelectedOptionText, DecisionAnswerV1? DecisionAnswer, string? AnswerMarkdown, string? ExternalUrl,
    AttachmentInfo? Attachment, FeedbackDto? Feedback, ReflectionDto? Reflection, bool CanAddReflection);
public sealed record AttachmentInfo(Guid AttachmentId, string FileName, string ContentType, long SizeBytes);
public sealed record FeedbackDto(ReviewVerdict Verdict, string StrengthsMarkdown, string ImprovementsMarkdown, string ChallengeQuestionMarkdown, DateTime CreatedAtUtc);
public sealed record ReflectionDto(int ConfidenceAfter, string TakeawayMarkdown, DateTime CreatedAtUtc);
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType);
```
`FeedbackDto`/`ReflectionDto` są zdefiniowane w `Submissions/Contracts` (A3), ale ich dane zapisuje A4 — A3 tylko czyta.

### 5.6. Reviews & Reflections (A4)

```csharp
public interface IReviewService
{
    Task<Result<IReadOnlyList<PendingReviewItem>>> GetPendingAsync(Guid actorUserId, CancellationToken ct); // FIFO po SubmittedAtUtc
    Task<Result<ReviewDetailDto>> GetReviewDetailAsync(Guid actorUserId, Guid submissionId, CancellationToken ct);
    Task<Result<Guid>> SubmitFeedbackAsync(Guid actorUserId, SubmitFeedbackRequest request, CancellationToken ct);
}
public sealed record PendingReviewItem(Guid SubmissionId, Guid MentorshipId, string MenteeDisplayName, string ExerciseTitle,
    ExerciseType Type, int AttemptNumber, DateTime SubmittedAtUtc, DateTime? DueAtUtc);
public sealed record ReviewDetailDto(Guid SubmissionId, Guid MentorshipId, string MenteeDisplayName, SubmissionStatus Status,
    MentorExerciseDto Exercise, SubmissionHistoryDto History);   // Exercise zawiera Rubric + MentorGuide (widok mentora)
public sealed record SubmitFeedbackRequest(Guid SubmissionId, string StrengthsMarkdown, string ImprovementsMarkdown,
    string ChallengeQuestionMarkdown, ReviewVerdict Verdict);    // wszystkie pola wymagane

public interface IReflectionService
{
    Task<Result> SaveReflectionAsync(Guid actorUserId, SaveReflectionRequest request, CancellationToken ct);
}
public sealed record SaveReflectionRequest(Guid SubmissionId, int ConfidenceAfter, string TakeawayMarkdown);
```
Refleksja: tylko autor-mentee, tylko dla statusów `Correct/Incorrect/Accepted/NeedsRevision`, jednorazowa (D-15). Błędy: `review.already_reviewed` (Conflict), `review.not_pending` (Conflict), `reflection.already_exists` (Conflict).

### 5.7. Progress (A4)

```csharp
public interface IProgressService
{
    Task<Result<RoadmapProgressDto>> GetRoadmapProgressAsync(Guid actorUserId, Guid mentorshipId, CancellationToken ct); // mentor lub mentee
    Task<Result<MentorDashboardDto>> GetMentorDashboardAsync(Guid actorUserId, CancellationToken ct);
}
public enum AssignmentProgressStatus { NotStarted, InProgress, PendingReview, NeedsRevision, Completed }
public enum TopicProgressStatus { NotStarted, InProgress, Completed }

public sealed record RoadmapProgressDto(Guid MentorshipId, bool HasPublishedRoadmap, int CompletedTopics, int CountedTopics,
    int PercentComplete, IReadOnlyList<TopicProgressDto> Topics);
public sealed record TopicProgressDto(Guid RoadmapItemId, string TopicTitle, TopicProgressStatus Status, bool CountsTowardsProgress,
    int CompletedRequired, int TotalRequired, IReadOnlyList<AssignmentProgressDto> Assignments);
public sealed record AssignmentProgressDto(Guid AssignmentId, string ExerciseTitle, bool IsRequired, AssignmentProgressStatus Status,
    int? LatestConfidenceBefore, int? LatestConfidenceAfter);

public sealed record MentorDashboardDto(IReadOnlyList<MenteeProgressRow> Mentees, IReadOnlyList<PendingReviewItem> PendingReviews,
    IReadOnlyList<ActivityItem> RecentActivity);
public sealed record MenteeProgressRow(Guid MentorshipId, string MenteeDisplayName, MentorshipStatus Status, bool HasPublishedRoadmap,
    int CompletedTopics, int CountedTopics, int PendingReviewCount, DateTime? LastSubmissionAtUtc);
public enum ActivityKind { Submitted, FeedbackGiven, ReflectionAdded }
public sealed record ActivityItem(DateTime OccurredAtUtc, ActivityKind Kind, Guid MentorshipId, string MenteeDisplayName, string ExerciseTitle, Guid SubmissionId);

// Czysta funkcja — jedyne miejsce reguł PRG-02..04 (A4)
public static class ProgressCalculator
{
    public static AssignmentProgressStatus ForAssignment(IReadOnlyList<SubmissionStatus> statusesOldestFirst);
    public static TopicProgressStatus ForTopic(IReadOnlyList<(bool IsRequired, AssignmentProgressStatus Status)> activeAssignments);
}
```
Reguły (D-16): Assignment Completed ⇔ istnieje `Correct` lub `Accepted`. Temat (aktywny) Completed ⇔ wszystkie aktywne wymagane przypisania Completed; temat bez aktywnych wymaganych przypisań: `CountsTowardsProgress = false`. `PercentComplete = CountedTopics == 0 ? 0 : floor(100 * Completed / Counted)`. Nic nie jest przechowywane. Ostatnia aktywność: 20 zdarzeń z 30 dni, wyliczane z Submissions/MentorFeedback/SubmissionReflections.

Komponenty UI A4 osadzane przez A3: `<RoadmapProgressSummary MentorshipId="..." />`, `<ReflectionForm SubmissionId="..." OnSaved="..." />`.

---

## 6. Maszyna stanów (zamrożona)

```
Exercise:   Draft ──Publish──▶ Published ──Archive──▶ Archived
            Draft ──Delete (hard)        Published ──Clone──▶ nowy Draft
Roadmap:    Draft ──Publish──▶ Published   (brak cofnięcia publikacji)
Submission: Quiz:     insert → Correct | Incorrect                  (A3)
            AD / PT:  insert → PendingReview ──Feedback──▶ Accepted | NeedsRevision   (A4)
Mentorship: Active ⇄ Paused, Active|Paused → Closed, Closed → Active (mentor)          (A1, D-12)
```
Wysłanie nowej próby dozwolone ⇔ mentee jest autorem relacji **i** `Mentorship.Status == Active` **i** roadmapa `Published` **i** item + assignment aktywne **i** typ ćwiczenia zgodny z metodą **i** brak próby `Correct`/`Accepted` **i** brak próby `PendingReview`. `DueAtUtc` nie blokuje (informacyjny).

---

## 7. Współbieżność i idempotencja (wzorce obowiązkowe)

| Operacja | Mechanizm |
|---|---|
| Submit (SUB-04/05/06) | Transakcja: `AttemptNumber = max + 1`; insert. Naruszenie `(MenteeUserId, ClientRequestId)` → zwróć istniejącą próbę z `WasDuplicate = true` (jeśli inny AssignmentId → `submission.client_request_reused`). Naruszenie `(AssignmentId, AttemptNumber)` lub partial-unique PendingReview → `submission.concurrent_attempt` (Conflict). Upload: przy duplikacie usuń właśnie zapisany obiekt. |
| Feedback (inv. 10) | Jedna transakcja: `ExecuteUpdate Submissions SET Status=@verdict WHERE Id=@id AND WorkspaceId=@ws AND Status='PendingReview'` → 0 wierszy = Conflict; insert `MentorFeedback` (unique SubmissionId). |
| Akceptacja zaproszenia | `ExecuteUpdate Invitations SET AcceptedAtUtc=now WHERE Id=@id AND AcceptedAtUtc IS NULL AND ExpiresAtUtc > now` → 0 = `invitation.invalid_or_used`; insert Mentorship (unique). Wszystko w jednej transakcji z utworzeniem konta. |
| Publikacja ćwiczenia | `ExecuteUpdate ... WHERE Status='Draft'`. |
| Limit AI | count-then-insert; dopuszczalne przekroczenie o 1 przy wyścigu (tylko mentor) — znane ryzyko. |

---

## 8. Integracja i współdzielone pliki

- Pliki `Contracts/` każdego modułu tworzy **A1 w M1** dokładnie wg §2–§5 (kompilują się, bez implementacji). Po M1 właścicielem pliku kontraktu jest właściciel modułu, ale zmiana = CR.
- A1 w M1 tworzy też: `<Module>Module.cs` (puste `Add<X>Module`) dla wszystkich modułów, wywołania w `AddMentorPathModules()`, stuby `RoadmapProgressSummary.razor` i `ReflectionForm.razor`, nawigację dla wszystkich tras z spec §7. Następnie własność plików przechodzi na właścicieli modułów.
- Pakiety NuGet: wyłącznie A1 (`Directory.Packages.props`, csproj). A1 dodaje w M1 wszystkie przewidziane pakiety (Npgsql EF, Identity EF, Markdig, HtmlSanitizer, AWSSDK.S3, Testcontainers.PostgreSql/Minio, Microsoft.Extensions.TimeProvider.Testing, Shouldly, Microsoft.AspNetCore.Mvc.Testing).
- `TestDataBuilder` — klasa `partial`; A1 tworzy rdzeń, każdy agent dodaje własny plik `TestDataBuilder.<Module>.cs`.
