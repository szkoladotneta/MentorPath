# MentorPath — Security Review (MVP, B16/B17)

Data: 2026-10-17 · Zakres: kod w `src/`, konfiguracja hosta, obraz Docker. Każdy punkt ma dowód (test lub miejsce w kodzie).

## 1. Wyniki przeglądu

| Obszar | Status | Mechanizm | Dowód |
|---|---|---|---|
| IDOR / izolacja workspace'ów | ✅ | `WorkspaceId` wyliczany z aktora/zasobu (D-08); każda metoda serwisu sprawdza `IWorkspaceAccessService`; brak dostępu = 404; złożone FK `(WorkspaceId, ParentId)` | `Access/*`, `*/…isolated*`, `Release/ReleaseScenarioTests` (krok 12), `Http/Mentor_pages_do_not_render_foreign_workspace_data` |
| Rola ≠ uprawnienie | ✅ | `[Authorize(Roles)]` tylko jako bramka UI; serwisy sprawdzają zasób | `Isolation/ArchitectureTests.Mentor_and_mentee_pages_require_role_authorization` + testy serwisów z aktorem o złej roli |
| Wyciek odpowiedzi quizu / mentor guide / notatek | ✅ | Osobne typy DTO dla mentee bez tych pól; wyjaśnienie dopiero po poprawnej próbie | `ArchitectureTests.Mentee_dto_types_…`, `SubmissionTests.Quiz_is_auto_graded…`, HTTP test sprawdzający HTML |
| XSS (Markdown) | ✅ | Markdig `DisableHtml()` + HtmlSanitizer (bez `img`, `style`, schematy http/https/mailto); jedyne miejsce `MarkupString` to komponent `Markdown` | `UnitTests/Common/SecurityHelpersTests` |
| XSS (dane AI) | ✅ | Odpowiedź AI z HTML jest odrzucana przed zapisem; to samo dla szkiców agentów MCP (`ExerciseSource.AI`, D-64) | `AiDraftValidatorTests.Html_in_output_is_rejected`, `McpToolsTests.Agent_drafts_with_html_are_rejected` |
| CSRF | ✅ | Antiforgery dla formularzy SSR, logout przez POST, endpoint uploadu wymaga nagłówka `X-CSRF-TOKEN`; akcje interaktywne idą przez obwód SignalR powiązany z cookie | `HttpPipelineTests.Upload_requires_antiforgery…` |
| Upload | ✅ | ≤ 10 MB, allowlista rozszerzeń i Content-Type, sygnatury PDF/PNG/JPG, UTF-8 bez NUL dla TXT/MD, losowy `ObjectKey`, nazwa pliku sanityzowana | `AttachmentValidatorTests`, `SubmissionTests.Practical_task_accepts…` |
| Download | ✅ | Tylko autor i właściciel workspace'u; `Content-Disposition: attachment`, `nosniff`, `Cache-Control: no-store`, CSP `sandbox` | `HttpPipelineTests.Upload_…download_is_restricted…` |
| Zewnętrzne URL | ✅ | Tylko `http(s)`, nigdy nie pobierane przez serwer, `rel="noopener noreferrer nofollow"` | `SecurityHelpersTests.Only_http_urls_are_accepted` |
| Tokeny zaproszeń | ✅ | 32 B CSPRNG, w bazie tylko SHA-256, ważność 7 dni, jednorazowe (warunkowy UPDATE), zgodność e-maila | `InvitationTests` (w tym współbieżna akceptacja) |
| Uwierzytelnianie | ✅ | Identity, cookie `HttpOnly` + `SameSite=Lax` + `Secure` (Production), lockout 5/15 min, hasło ≥ 10 znaków, brak publicznej rejestracji | `HttpPipelineTests.Auth_cookie_is_http_only`, `Anonymous_users_…` (`/Account/Register` → 404) |
| Rate limiting | ✅ | POST `/Account/*` i `/invite/*`: 20/min/IP; upload: 10/min/użytkownik; AI: limit miesięczny per workspace; MCP `/mcp`: 60/min/mentora (D-64) | `WebComposition`, `AiQuotaTests`, `McpHttpTests.Requests_are_rate_limited_per_mentor` |
| Klucz AI / PII w logach | ✅ | Klucz tylko z konfiguracji serwera; logowany status/model/tokeny/kod błędu; do AI trafiają tylko dane katalogu i wejście mentora. Przez MCP (D-64) do klienta AI mentora trafiają dodatkowo odpowiedzi onboardingowe mentee i postęp — nigdy e-mail ani prywatne notatki mentora | `AiExerciseDraftService`, `ExerciseDraftPrompt.BuildUserPrompt`, `ArchitectureTests.Mcp_result_types_never_expose_mentee_email_or_private_notes` |
| Nagłówki | ✅ | `X-Content-Type-Options`, `X-Frame-Options: DENY`, CSP `frame-ancestors 'none'; object-src 'none'; base-uri 'self'; form-action 'self'`, `Referrer-Policy`, `Permissions-Policy`, HSTS (Production) | `HttpPipelineTests.Health_endpoints_and_security_headers` |
| Współbieżność / integralność | ✅ | Jedna próba PendingReview (partial unique), idempotencja `ClientRequestId`, jeden feedback (warunkowy UPDATE + unique) | `SubmissionTests`, `ReviewTests.Concurrent_reviews_…`, `SchemaConstraintTests` |
| Sekrety w repo | ✅ | Brak; wartości w compose/`appsettings.Development.json` to lokalne domyślne | przegląd plików |
| Kontener | ✅ | Obraz `aspnet:10.0`, użytkownik nie-root (`$APP_UID`) | `Dockerfile` |

## 2. Ryzyka rezydualne (otwarte)

| # | Ryzyko | Priorytet | Rekomendacja |
|---|---|---|---|
| S1 | Link zaproszenia przekazany niewłaściwej osobie pozwala jej założyć konto na e-mail z zaproszenia (posiadanie tokenu = weryfikacja e-maila, D-11) | Średni | Mentor przekazuje link prywatnym kanałem; w P1 — weryfikacja e-mailem |
| S2 | ~~Brak procedury eksportu/usuwania danych osobowych~~ — **rozwiązane (N04, D-42)**: CLI `export-user` / `anonymize-user`, procedura i retencja w `docs/DATA_RETENTION.md`. Brak UI dla osoby (prośby obsługuje operator) | Niski | Rejestr próśb prowadzi operator poza aplikacją; usunięcie konta mentora — osobna decyzja |
| S3 | Osierocone bloby przy awarii procesu między zapisem pliku a commitem DB (D-20, D-47) | Niski | Okresowe porównanie kluczy kontenera Azure Blob z `SubmissionAttachments` |
| S4 | Wyścig limitu AI może przekroczyć limit o 1 | Niski | Akceptowalne (tylko mentor) |
| S5 | CSP bez `script-src` | Niski | Zaostrzyć po weryfikacji zgodności z Blazor (`script-src 'self'`) | — **zamknięte (T02, D-58)**
| S6 | Klucze Data Protection na dysku bez szyfrowania | Niski | `ProtectKeysWith…` (KMS / certyfikat) w środowisku docelowym | — **zamknięte (T02, D-58)**
| S7 | Mentor może wpisać dane osobowe w polu kontekstu AI | Niski | Ostrzeżenie w UI istnieje; decyzja o dostawcy AI i umowie powierzenia (D-19) |
| S8 | RODO: umowy z dostawcami (hosting, SMTP, AI), polityka prywatności (retencja: `docs/DATA_RETENTION.md`) | Wymagane przed produkcją | Zadanie organizacyjne, poza kodem |
| S9 | MCP (D-64): odpowiedzi onboardingowe mentee (cel, wyzwania) i postęp trafiają do klienta AI wybranego przez mentora, poza kontrolą aplikacji; tokeny MCP mają pełne prawa mentora (do 90 dni; unieważnia je reset hasła, blokada konta lub utrata roli — sprawdzane przy każdym żądaniu, `McpHttpTests`) | Średni | Decyzja o dopuszczalnych klientach i umowa powierzenia (jak S8) przed ustawieniem `Mcp__SigningKey` na produkcji; rozważyć zgodę mentee w onboardingu |
