# MentorPath — MVP Specification v1.1

**Typ produktu:** Platforma do indywidualnego mentoringu architektury backendowej  
**Główny obszar:** .NET / backend architecture  
**Model:** Wiele niezależnych workspace'ów mentorów  
**Frontend:** Blazor Web App, Interactive Server  
**Backend:** ASP.NET Core / .NET 10 LTS  
**Baza:** PostgreSQL + EF Core  
**Architektura:** Modular Monolith  
**Status:** Specyfikacja do implementacji

---

# 1. Cel produktu

MentorPath pomaga developerom rozwijać umiejętność samodzielnego podejmowania decyzji architektonicznych.

System organizuje proces:

**Scenariusz → Decyzja → Uzasadnienie → Feedback → Refleksja → Postęp**

Platforma nie jest tradycyjnym kursem wideo ani systemem automatycznego sprawdzania kodu.

Mentor odpowiada za jakość ścieżki nauki i feedbacku. AI pomaga mentorowi przygotowywać treści, ale nie zastępuje go.

## 1.1. Cele MVP

MVP musi pozwolić przeprowadzić pełny cykl mentoringowy:

1. Operator zakłada konto mentora.
2. Mentor otrzymuje własny workspace.
3. Mentor zaprasza mentee.
4. Mentee aktywuje konto i podaje cele.
5. Mentor tworzy tematy i ćwiczenia, ręcznie lub z pomocą AI.
6. Mentor buduje i publikuje indywidualną roadmapę.
7. Mentee rozwiązuje quizy i zadania architektoniczne.
8. Mentee przesyła link do repozytorium lub plik.
9. Mentor ocenia rozwiązanie i przekazuje feedback.
10. Mentee poprawia rozwiązanie lub zapisuje refleksję.
11. System pokazuje postęp.

## 1.2. Granica MVP

**W zakresie:**

- Wiele niezależnych workspace'ów.
- Jeden właściciel-mentor na workspace.
- Zaproszenia mentees.
- Profile mentoringowe.
- Katalog tematów.
- Trzy typy ćwiczeń.
- Generowanie szkiców ćwiczeń przez AI.
- Ręczna akceptacja treści AI.
- Personalizowane roadmapy.
- Przesyłanie odpowiedzi i artefaktów.
- Manualny feedback.
- Historia prób.
- Postęp i samoocena pewności.
- Podstawowe dashboardy.

**Poza zakresem:**

- Publiczna rejestracja mentorów.
- Płatności.
- Organizacje i zespoły mentorów.
- Współdzielenie katalogów między mentorami.
- Marketplace.
- AI review rozwiązań mentee.
- AI-generated roadmap.
- Wbudowane IDE.
- Uruchamianie kodu.
- Integracja OAuth z GitHub.
- Synchronizacja repozytoriów.
- Wideorozmowy i kalendarz.
- Rozbudowane powiadomienia.
- System czatu.
- Ranking i gamifikacja.

---

# 2. Model workspace'ów i role

## 2.1. Workspace

Każdy mentor posiada własny workspace.

Workspace jest granicą dostępu do:

- Mentees.
- Tematów.
- Ćwiczeń.
- Roadmap.
- Rozwiązań.
- Feedbacku.
- Historii generowania treści przez AI.

W MVP workspace ma jednego właściciela.

Konto mentee może być przypisane do więcej niż jednego workspace'u. Dane każdej relacji pozostają odseparowane.

Nie stosujemy osobnej bazy danych na workspace. Wszystkie workspace'y korzystają z jednej bazy PostgreSQL, a dane są logicznie izolowane.

## 2.2. Role

### PlatformOperator

Rola operacyjna, bez panelu administracyjnego w MVP.

- Zakłada konta mentorów za pomocą skryptu lub narzędzia administracyjnego.
- Tworzy workspace dla mentora.
- Zarządza infrastrukturą.
- Nie przegląda rozwiązań mentees w codziennym przepływie produktu.

### Mentor

Właściciel workspace'u.

- Zaprasza mentees.
- Zarządza własnym katalogiem.
- Tworzy i zatwierdza treści AI.
- Buduje roadmapy.
- Ocenia rozwiązania.
- Widzi postępy swoich mentees.

### Mentee

Uczestnik relacji mentoringowej.

- Widzi wyłącznie własne przypisania.
- Wykonuje ćwiczenia.
- Przesyła rozwiązania.
- Odczytuje feedback.
- Zapisuje refleksje.

**Ważne:** globalna rola Identity nie daje dostępu do zasobu. Każda operacja sprawdza również właściciela workspace'u lub uczestnictwo w relacji mentoringowej.

## 2.3. Reguły izolacji

- Mentor A nie może zobaczyć mentees Mentora B.
- Mentor A nie może edytować katalogu Mentora B.
- Mentor A nie może ocenić rozwiązania z innego workspace'u.
- Mentee A nie może zobaczyć rozwiązania Mentee B.
- Mentee przypisana do dwóch mentorów widzi dwie odrębne przestrzenie mentoringowe.
- Sam identyfikator zasobu w URL nigdy nie jest wystarczającym dowodem uprawnienia.

---

# 3. Moduły i wymagania funkcjonalne

## 3.1. Identity & Invitations

**IAM-01 — Konto mentora**

Operator może utworzyć konto mentora i jego workspace przez komendę administracyjną.

Nie istnieje publiczny formularz rejestracji mentorów.

**IAM-02 — Zaproszenie mentee**

Mentor podaje adres e-mail.

System generuje jednorazowy token ważny przez 7 dni.

W bazie przechowywany jest wyłącznie hash tokenu.

Mentor może skopiować link i przesłać go samodzielnie.

**IAM-03 — Akceptacja zaproszenia**

- Nowa osoba tworzy konto.
- Istniejąca osoba loguje się na swoje konto.
- System dodaje mentee do właściwego workspace'u.
- Token nie może zostać wykorzystany ponownie.
- Konto nie może przyjąć zaproszenia skierowanego do innego adresu e-mail bez odpowiedniej weryfikacji.

**IAM-04 — Logowanie**

ASP.NET Core Identity z cookies.

Wymagane: logowanie, wylogowanie, reset hasła, lockout i bezpieczne cookies.

**IAM-05 — Wybór mentoringu**

Jeżeli mentee ma dostęp do więcej niż jednego workspace'u, po zalogowaniu wybiera aktywną relację mentoringową.

## 3.2. Mentorship Profile

**MEN-01 — Onboarding mentee**

Formularz:

- Cel mentoringu.
- Poziom doświadczenia.
- Największe wyzwania.
- Dostępne godziny tygodniowo.

**MEN-02 — Notatki mentora**

Mentor może dodać prywatne notatki o potrzebach edukacyjnych mentee.

Notatki nie są widoczne dla mentee i nie są wysyłane do AI w MVP.

## 3.3. Topic Catalog

**CAT-01 — Zarządzanie tematami**

Mentor może tworzyć, edytować, przeglądać i archiwizować tematy.

Pola:

- Title.
- Slug.
- Category.
- Difficulty.
- Description.
- Learning Objectives.

Katalog jest prywatny dla workspace'u.

**CAT-02 — Powiązanie ćwiczeń**

Temat może zawierać wiele ćwiczeń.

## 3.4. Exercise Authoring

**EX-01 — Typy ćwiczeń**

### QuizSingleChoice

- Scenariusz.
- Minimum dwie opcje.
- Dokładnie jedna poprawna.
- Wyjaśnienie odpowiedzi.
- Obowiązkowe uzasadnienie wyboru przez mentee.

### ArchitecturalDecision

- Scenariusz.
- Ograniczenia i wymagania.
- Oczekiwane rezultaty.
- Kryteria oceny.

Odpowiedź mentee jest ustrukturyzowana:

1. Decision.
2. Rationale.
3. Alternatives.
4. Trade-offs.
5. Risks.
6. Conditions for Reconsideration.

### PracticalTask

- Opis zadania.
- Wymagania.
- Oczekiwane artefakty.
- Kryteria oceny.
- Pytania dotyczące decyzji implementacyjnych.

Rozwiązanie powstaje poza aplikacją.

**EX-02 — Statusy treści**

- Draft.
- Published.
- Archived.

Ćwiczenie opublikowane jest niemodyfikowalne pod względem treści merytorycznej.

Aby je zmienić, mentor tworzy kopię jako nowy draft.

Zapobiega to zmianie znaczenia historycznych rozwiązań.

**EX-03 — Mentor Guide**

Każde ćwiczenie może zawierać prywatną instrukcję dla mentora.

Nigdy nie jest wysyłana do widoku mentee.

## 3.5. AI-Assisted Exercise Authoring

**AI-01 — Generate Draft**

Mentor wybiera:

- Temat.
- Typ ćwiczenia.
- Poziom trudności.
- Cele edukacyjne.
- Kontekst lub ograniczenia.
- Szacowany czas wykonania.

Kliknięcie `Generate with AI` uruchamia generowanie szkicu.

**AI-02 — Structured Output**

AI zwraca ustrukturyzowane dane zawierające:

- Tytuł.
- Scenariusz.
- Instrukcje.
- Kryteria oceny.
- Mentor guide.
- Wyjaśnienie.
- Opcje quizu, jeśli dotyczy.
- Pytania pogłębiające.

Wynik musi przejść walidację schematu i reguł domenowych.

**AI-03 — Human Approval**

Wygenerowana treść jest zawsze zapisywana jako `Draft`.

Mentor może ją:

- Edytować.
- Odrzucić.
- Opublikować.

Publikacja wymaga jawnej akcji mentora.

**AI-04 — Provider Failure**

Jeżeli dostawca AI nie odpowiada, mentor widzi czytelny błąd i może kontynuować tworzenie ręczne.

**AI-05 — Privacy & Cost**

- Klucz API wyłącznie na serwerze.
- Nie wysyłać danych osobowych mentees.
- Nie wysyłać prywatnych notatek mentora.
- Nie wysyłać rozwiązań mentees.
- Konfigurowalny limit generowań na workspace.
- Logować status i zużycie tokenów, jeśli dostępne.
- Nie logować pełnych promptów z potencjalnymi danymi osobowymi.

**AI-06 — Scope**

W MVP AI generuje szkice ćwiczeń, nie całe roadmapy i nie feedback.

## 3.6. Roadmaps

**RDM-01 — Utworzenie roadmapy**

Mentor tworzy roadmapę dla konkretnej relacji mentoringowej.

Roadmapa zawiera uporządkowane tematy.

**RDM-02 — Assignments**

Mentor przypisuje do tematu opublikowane ćwiczenia.

Każde przypisanie ma:

- Required / Optional.
- Opcjonalny termin.
- Pozycję w temacie.

**RDM-03 — Publikacja**

Mentee widzi tylko opublikowaną roadmapę.

Roadmapa musi mieć co najmniej jeden temat i jedno wymagane ćwiczenie.

**RDM-04 — Edycja**

Mentor może dodawać tematy i zmieniać kolejność.

Nie wolno usuwać historycznych przypisań z rozwiązaniami. Można je oznaczyć jako wycofane z aktywnej ścieżki.

**RDM-05 — Liczba roadmap**

W MVP jedna roadmapa na relację mentoringową.

## 3.7. Submissions

**SUB-01 — Quiz**

Mentee wybiera odpowiedź, podaje uzasadnienie i confidence 1–5.

System sprawdza poprawność.

Quiz można ponawiać do uzyskania poprawnej odpowiedzi.

**SUB-02 — Architectural Decision**

Mentee wypełnia sześć pól decyzji architektonicznej i ocenia confidence.

Próba otrzymuje status `PendingReview`.

**SUB-03 — Practical Task**

Mentee przesyła:

- Link HTTP(S) do repozytorium lub artefaktu, lub
- Jeden załącznik.

Dodatkowo wpisuje uzasadnienie decyzji i confidence.

Nie pobieramy automatycznie zawartości repozytorium.

**SUB-04 — Historia prób**

Każda wysyłka tworzy nową próbę.

Przesłanej próby nie można edytować.

**SUB-05 — Idempotencja**

Każde wysłanie ma `ClientRequestId`.

Powtórzone żądanie nie tworzy drugiej próby.

**SUB-06 — Jedna aktywna próba**

Dla zadań ocenianych ręcznie nie można przesłać kolejnej próby, dopóki poprzednia ma status `PendingReview`.

## 3.8. Reviews

**REV-01 — Review Queue**

Mentor widzi oczekujące rozwiązania tylko ze swojego workspace'u.

**REV-02 — Feedback**

Feedback zawiera:

- Strengths.
- Improvements.
- Challenge Question.
- Verdict: Accepted / NeedsRevision.

**REV-03 — Powtórzenie zadania**

Po `NeedsRevision` mentee może utworzyć kolejną próbę.

Po `Accepted` zadanie jest ukończone.

**REV-04 — Rubric**

Mentor widzi kryteria oceny podczas review.

W MVP nie wprowadzamy obowiązkowej punktowej skali ocen. Najważniejszy jest jakościowy feedback.

## 3.9. Reflection & Progress

**PRG-01 — Reflection**

Po review mentee może zapisać:

- Confidence After: 1–5.
- Najważniejszy wniosek.

Refleksja jest przypisana do konkretnej próby.

**PRG-02 — Zaliczenie zadania**

Quiz: istnieje próba `Correct`.

Zadanie otwarte: istnieje próba `Accepted`.

**PRG-03 — Zaliczenie tematu**

Wszystkie aktywne wymagane zadania są zaliczone.

**PRG-04 — Postęp roadmapy**

Liczba ukończonych aktywnych tematów / liczba wszystkich aktywnych tematów.

Postęp jest wyliczany, nie przechowywany jako niezależny stan.

**PRG-05 — Confidence**

Confidence jest samooceną, a nie obiektywną oceną kompetencji.

---

# 4. Statusy i reguły domenowe

## ExerciseStatus

- Draft
- Published
- Archived

## RoadmapStatus

- Draft
- Published

## SubmissionStatus

- Correct
- Incorrect
- PendingReview
- Accepted
- NeedsRevision

## MentorshipStatus

- Active
- Paused
- Closed

## AIRequestStatus

- Pending
- Completed
- Failed

## Kluczowe invariants

1. Każdy workspace ma dokładnie jednego właściciela w MVP.
2. Ćwiczenie i temat należą do tego samego workspace'u.
3. Przypisane ćwiczenie należy do tematu wskazanego przez roadmap item.
4. Rozwiązanie może wysłać tylko mentee będąca uczestnikiem danej relacji.
5. Feedback może wystawić tylko właściciel właściwego workspace'u.
6. Quiz ma dokładnie jedną poprawną opcję.
7. Ćwiczenia nie można przypisać przed publikacją.
8. AI draft nie może zostać automatycznie opublikowany.
9. Historyczne rozwiązania i feedback pozostają niezmienne.
10. Dwie równoczesne operacje review nie mogą utworzyć dwóch feedbacków dla tej samej próby.

---

# 5. Model danych

Standardowe tabele ASP.NET Core Identity nie są wyszczególnione.

## 5.1. Workspaces

- Id: uuid PK
- OwnerUserId: uuid FK Users, unique
- Name: varchar(200)
- CreatedAtUtc: timestamptz

## 5.2. Mentorships

- Id: uuid PK
- WorkspaceId: uuid FK
- MenteeUserId: uuid FK
- Goal: text nullable
- CurrentLevel: varchar(40) nullable
- WeeklyHours: int nullable
- Challenges: text nullable
- MentorPrivateNotes: text nullable
- Status: varchar(20)
- CreatedAtUtc: timestamptz
- UpdatedAtUtc: timestamptz

Unique: (WorkspaceId, MenteeUserId).

Nie stosować unikalności samego MenteeUserId — użytkownik może mieć wielu mentorów.

## 5.3. Invitations

- Id: uuid PK
- WorkspaceId: uuid FK
- InvitedByUserId: uuid FK
- NormalizedEmail: varchar(320)
- TokenHash: varchar(64), unique
- ExpiresAtUtc: timestamptz
- AcceptedAtUtc: timestamptz nullable
- CreatedAtUtc: timestamptz

## 5.4. Topics

- Id: uuid PK
- WorkspaceId: uuid FK
- Slug: varchar(150)
- Title: varchar(200)
- Category: varchar(80)
- Difficulty: varchar(30)
- DescriptionMarkdown: text
- LearningObjectivesMarkdown: text
- IsArchived: boolean
- CreatedAtUtc: timestamptz

Unique: (WorkspaceId, Slug).

## 5.5. Exercises

- Id: uuid PK
- WorkspaceId: uuid FK
- TopicId: uuid FK
- Type: varchar(30)
- Status: varchar(20)
- Source: varchar(20) — Manual / AI
- Title: varchar(200)
- InstructionsMarkdown: text
- RubricMarkdown: text
- MentorGuideMarkdown: text
- ExplanationMarkdown: text nullable
- EstimatedMinutes: int nullable
- PublishedAtUtc: timestamptz nullable
- CreatedAtUtc: timestamptz

## 5.6. QuizOptions

- Id: uuid PK
- ExerciseId: uuid FK
- Text: text
- IsCorrect: boolean
- ExplanationMarkdown: text
- Position: int

## 5.7. Roadmaps

- Id: uuid PK
- WorkspaceId: uuid FK
- MentorshipId: uuid FK, unique
- Title: varchar(200)
- ObjectiveMarkdown: text
- Status: varchar(20)
- PublishedAtUtc: timestamptz nullable
- CreatedAtUtc: timestamptz

## 5.8. RoadmapItems

- Id: uuid PK
- RoadmapId: uuid FK
- TopicId: uuid FK
- Position: int
- MentorNoteMarkdown: text nullable
- IsActive: boolean

Unique: (RoadmapId, TopicId).

## 5.9. Assignments

- Id: uuid PK
- WorkspaceId: uuid FK
- RoadmapItemId: uuid FK
- ExerciseId: uuid FK
- Position: int
- IsRequired: boolean
- IsActive: boolean
- DueAtUtc: timestamptz nullable

Unique: (RoadmapItemId, ExerciseId).

## 5.10. Submissions

- Id: uuid PK
- WorkspaceId: uuid FK
- AssignmentId: uuid FK
- MenteeUserId: uuid FK
- ClientRequestId: uuid
- AttemptNumber: int
- Status: varchar(30)
- SelectedQuizOptionId: uuid nullable
- DecisionAnswerJson: jsonb nullable
- AnswerMarkdown: text nullable
- ExternalUrl: text nullable
- ConfidenceBefore: smallint
- SubmittedAtUtc: timestamptz

Unique: (AssignmentId, AttemptNumber).

Unique: (MenteeUserId, ClientRequestId).

DecisionAnswerJson ma jawnie wersjonowany schemat DTO, zawierający sześć pól odpowiedzi architektonicznej.

## 5.11. SubmissionAttachments

- Id: uuid PK
- WorkspaceId: uuid FK
- SubmissionId: uuid FK, unique
- ObjectKey: varchar(500), unique
- OriginalFileName: varchar(255)
- ContentType: varchar(100)
- SizeBytes: bigint
- CreatedAtUtc: timestamptz

## 5.12. MentorFeedback

- Id: uuid PK
- WorkspaceId: uuid FK
- SubmissionId: uuid FK, unique
- MentorUserId: uuid FK
- Verdict: varchar(30)
- StrengthsMarkdown: text
- ImprovementsMarkdown: text
- ChallengeQuestionMarkdown: text
- CreatedAtUtc: timestamptz

## 5.13. SubmissionReflections

- Id: uuid PK
- SubmissionId: uuid FK, unique
- ConfidenceAfter: smallint
- TakeawayMarkdown: text
- CreatedAtUtc: timestamptz

## 5.14. AiGenerationRequests

- Id: uuid PK
- WorkspaceId: uuid FK
- RequestedByUserId: uuid FK
- ExerciseId: uuid FK nullable
- ExerciseType: varchar(30)
- Status: varchar(20)
- Provider: varchar(100)
- Model: varchar(100)
- PromptTemplateVersion: varchar(30)
- InputTokens: int nullable
- OutputTokens: int nullable
- ErrorCode: varchar(100) nullable
- CreatedAtUtc: timestamptz
- CompletedAtUtc: timestamptz nullable

Nie przechowywać surowych promptów zawierających dane osobowe.

## 5.15. Ograniczenia bazy

- Wszystkie klucze obce mają jawne zachowanie usuwania.
- Historyczne submissions i feedback nie są usuwane kaskadowo przy edycji roadmapy.
- Zakres confidence: 1–5.
- Dodatnie AttemptNumber.
- Jedna poprawna opcja quizu — walidacja aplikacyjna i, gdzie możliwe, częściowy indeks unikalny.
- Spójność WorkspaceId i powiązanych encji — walidacja domenowa oraz odpowiednie ograniczenia relacyjne.
- Indeksy po WorkspaceId na tabelach używanych do filtrowania.
- Indeks kolejki review: (WorkspaceId, Status, SubmittedAtUtc).

---

# 6. Architektura aplikacji

## 6.1. Stos

- .NET 10 LTS.
- ASP.NET Core.
- Blazor Web App — Interactive Server.
- PostgreSQL.
- EF Core.
- ASP.NET Core Identity.
- Prywatny S3-compatible object storage.
- xUnit.
- Testcontainers PostgreSQL dla testów integracyjnych.
- GitHub Actions.
- Docker.

## 6.2. Podejście

Jeden proces aplikacyjny i jedna baza danych.

Moduły:

- Identity
- Workspaces
- Mentorship
- Catalog
- AI Authoring
- Roadmaps
- Submissions
- Reviews
- Progress

Nie tworzyć:

- Microservices.
- Event busa.
- Generic repository.
- Frameworku CQRS.
- Oddzielnego REST API dla każdego ekranu Blazor.
- Rozbudowanego systemu pluginów.

## 6.3. Dostęp do danych

Dla operacji Blazor Interactive Server używać krótkotrwałych kontekstów tworzonych przez `IDbContextFactory<AppDbContext>`.

Nie współdzielić śledzonego DbContext przez długowieczny circuit Blazor.

Każdy serwis aplikacyjny sprawdza uprawnienia do workspace'u.

## 6.4. Kontrakty między modułami

### IWorkspaceAccessService

- CanManageWorkspaceAsync(userId, workspaceId)
- CanAccessMentorshipAsync(userId, mentorshipId)
- CanAccessSubmissionAsync(userId, submissionId)

### IExerciseAuthoringService

- CreateDraftAsync
- UpdateDraftAsync
- PublishAsync
- CloneAsDraftAsync
- GetMentorExerciseAsync
- GetMenteeExerciseAsync

### IAiExerciseDraftService

- GenerateDraftAsync

### IRoadmapService

- CreateAsync
- AddTopicAsync
- AssignExerciseAsync
- PublishAsync
- GetForMenteeAsync

### ISubmissionService

- SubmitQuizAsync
- SubmitDecisionAsync
- SubmitPracticalAsync
- GetHistoryAsync

### IReviewService

- GetPendingAsync
- SubmitFeedbackAsync

### IProgressService

- GetRoadmapProgressAsync
- GetMenteeDashboardAsync
- GetMentorDashboardAsync

### IFileStorage

- SaveAsync
- OpenReadAsync
- DeleteAsync

Wszystkie metody powinny korzystać z jawnych request/response DTO.

---

# 7. UI i layout

## 7.1. Design

Styl: profesjonalne narzędzie developerskie.

- Jasne tło.
- Czytelna typografia.
- Sidebar na desktopie.
- Nawigacja responsywna na mobile.
- Akcent indygo lub niebieski.
- Statusy i postęp przedstawione subtelnie.
- Bez rozbudowanej gamifikacji.

## 7.2. Mentor Routes

- `/mentor` — Dashboard.
- `/mentor/mentees` — Lista mentees.
- `/mentor/mentees/{id}` — Profil mentee.
- `/mentor/mentees/{id}/roadmap` — Edytor roadmapy.
- `/mentor/catalog/topics` — Katalog.
- `/mentor/catalog/topics/{id}` — Szczegóły tematu.
- `/mentor/exercises/new` — Nowe ćwiczenie.
- `/mentor/exercises/{id}/edit` — Edytor draftu.
- `/mentor/reviews` — Kolejka review.
- `/mentor/reviews/{id}` — Ocena rozwiązania.

## 7.3. Mentee Routes

- `/app` — Dashboard.
- `/app/mentorships` — Wybór mentoringu, gdy jest ich więcej niż jeden.
- `/app/roadmap/{mentorshipId}` — Roadmapa.
- `/app/assignments/{id}` — Zadanie.
- `/app/assignments/{id}/history` — Historia.
- `/app/progress/{mentorshipId}` — Postęp.
- `/app/profile` — Profil.

## 7.4. Mentor Dashboard

Sekcje:

1. Mentees i ich postęp.
2. Pending reviews.
3. Ostatnia aktywność.
4. Szybki dostęp do katalogu i tworzenia ćwiczeń.

## 7.5. AI Exercise Editor

Układ:

**Panel wejściowy:**

- Topic.
- Exercise type.
- Difficulty.
- Learning objectives.
- Constraints.
- Estimated time.
- Generate button.

**Panel rezultatu:**

- Edytowalny tytuł.
- Edytowalny scenariusz.
- Edytowalne kryteria.
- Quiz options, jeśli dotyczy.
- Prywatny mentor guide.
- Save Draft.
- Publish.

Status `AI Draft` jest informacyjny. Publikacja wymaga walidacji.

## 7.6. Mentee Assignment

Na desktopie dwie kolumny.

**Lewa:**

- Scenariusz.
- Wymagania.
- Oczekiwany rezultat.
- Kryteria.

**Prawa:**

- Formularz odpowiedzi.
- Uzasadnienie decyzji.
- Link lub upload.
- Confidence 1–5.
- Submit.

Na mobile układ jednokolumnowy.

## 7.7. Mentor Review

Dwie główne sekcje:

**Rozwiązanie:**

- Treść zadania.
- Odpowiedź.
- Link lub plik.
- Historia prób.
- Confidence Before.

**Ocena:**

- Rubric.
- Strengths.
- Improvements.
- Challenge Question.
- Accepted / NeedsRevision.

---

# 8. Upload i rozwiązania zewnętrzne

## 8.1. Zasady

- Kod wykonywany poza platformą.
- Linki do GitHub/GitLab/innych repozytoriów są zwykłymi URL.
- System nie klonuje repozytoriów.
- System nie uruchamia testów.
- System nie wykonuje kodu.
- Brak GitHub OAuth w MVP.

## 8.2. Pliki

- Maksymalnie jeden plik na submission.
- Maksymalnie 10 MB.
- Formaty: PDF, TXT, MD, PNG, JPG.
- Prywatny object storage.
- Losowy ObjectKey.
- Pobieranie tylko po autoryzacji.
- Brak publicznych URL.
- Wymuszony download jako attachment.
- Walidacja rozszerzenia, typu i sygnatury pliku.

Upload można obsłużyć przez dedykowany endpoint HTTP z uwierzytelnieniem cookie i ochroną CSRF, zamiast przesyłać duże pliki przez circuit Blazor.

---

# 9. AI — wymagania techniczne

## 9.1. Adapter

Implementacja AI za interfejsem serwisowym.

Konfiguracja:

- Provider.
- Model.
- API key.
- Timeout.
- Max output tokens.
- Limit generowań na workspace.

Nie wiązać logiki domenowej bezpośrednio z SDK jednego dostawcy.

## 9.2. Prompt systemowy

Generator powinien:

- Tworzyć realistyczne scenariusze architektoniczne.
- Wymuszać analizę kompromisów.
- Unikać pytań czysto definicyjnych.
- Uwzględniać kontekst .NET.
- Zwracać ustrukturyzowany JSON.
- Nie zakładać, że każde zadanie architektoniczne ma jedną poprawną odpowiedź.
- Wyraźnie rozdzielać treść dla mentee i prywatny mentor guide.

## 9.3. Walidacja

Przed zapisaniem draftu:

- Walidacja schematu JSON.
- Walidacja długości.
- Walidacja typu zadania.
- Walidacja liczby opcji quizu.
- Walidacja jednej poprawnej odpowiedzi.
- Walidacja pól wymaganych.
- Odrzucenie niedozwolonego HTML.

Dane wygenerowane przez AI są niezaufanym wejściem.

## 9.4. Odpowiedzialność

Mentor musi przejrzeć merytorycznie każdą wygenerowaną treść.

AI może popełnić błędy techniczne i zaproponować nieuzasadnione rozwiązania.

---

# 10. Bezpieczeństwo i prywatność

- Autoryzacja zasobowa w serwisach.
- Izolacja workspace'ów.
- HTTPS.
- Secure/HttpOnly cookies.
- CSRF protection.
- Rate limiting.
- Ochrona przed XSS przy Markdown.
- Brak sekretów w repozytorium.
- Brak danych osobowych w logach AI.
- Brak automatycznego pobierania zewnętrznych URL.
- Backup PostgreSQL.
- Ograniczenie dostępu do storage.
- Procedura eksportu i usuwania danych.
- Polityka prywatności i retencji.
- Weryfikacja wymagań RODO dla dostawców hostingu i AI.

---

# 11. Wymagania niefunkcjonalne

- Responsywny interfejs.
- Stany loading, empty i error.
- Walidacja formularzy po stronie klienta i serwera.
- Daty przechowywane w UTC.
- Angielskie nazwy w kodzie.
- Polski interfejs w MVP.
- Structured logging.
- Health endpoint.
- Migracje EF Core.
- Automatyczny build i testy w CI.
- Możliwość uruchomienia lokalnego przez Docker Compose.
- Konfiguracja sekretów przez zmienne środowiskowe lub secret store.

---

# 12. Scenariusze akceptacyjne

**AC-01 — Multi-workspace isolation**

Istnieją Mentor A i Mentor B.

Mentor A nie może odczytać ani zmodyfikować żadnego zasobu Mentora B.

**AC-02 — Invitation**

Mentee akceptuje ważne zaproszenie i zostaje przypisana do właściwego workspace'u.

Ponowne użycie tokenu jest odrzucane.

**AC-03 — AI draft**

Mentor generuje ćwiczenie.

Powstaje Draft.

Mentee nie widzi ćwiczenia, dopóki mentor go nie opublikuje i nie przypisze.

**AC-04 — AI failure**

Niedostępność AI nie blokuje ręcznego tworzenia ćwiczeń.

**AC-05 — Roadmap**

Mentor publikuje roadmapę z trzema tematami.

Mentee widzi tylko własną roadmapę.

**AC-06 — Quiz**

Mentee przesyła wybór, uzasadnienie i confidence.

System zapisuje próbę i ocenia poprawność.

Poprawna odpowiedź nie jest ujawniana w danych formularza przed wysłaniem.

**AC-07 — Practical task**

Mentee przesyła link lub plik i uzasadnienie.

Powstaje PendingReview.

**AC-08 — Feedback**

Mentor odsyła rozwiązanie do poprawy.

Mentee przesyła nową próbę.

Poprzednia próba i feedback pozostają dostępne.

**AC-09 — Progress**

Po zaakceptowaniu wszystkich wymaganych ćwiczeń temat staje się Completed.

**AC-10 — Idempotency**

Dwukrotne wysłanie tego samego ClientRequestId nie tworzy dwóch prób.

**AC-11 — Attachment authorization**

Użytkownik z innego workspace'u nie może pobrać załącznika.

**AC-12 — Reflection**

Mentee zapisuje Confidence After i wniosek dla ocenionej próby.

---

# 13. Definition of Done

Funkcjonalność jest gotowa, gdy:

1. Spełnia wymagania i scenariusze akceptacyjne.
2. Ma walidację serwerową.
3. Sprawdza autoryzację zasobową.
4. Obsługuje błędy.
5. Ma testy istotnych reguł biznesowych.
6. Nie narusza izolacji workspace'ów.
7. Przechodzi build i CI.
8. Nie zawiera sekretów ani danych testowych w produkcyjnej konfiguracji.
9. Ma odpowiednią migrację EF Core, jeżeli zmienia model.
10. Nie dodaje funkcjonalności spoza MVP.

**Koniec dokumentu MVP_SPEC.md**