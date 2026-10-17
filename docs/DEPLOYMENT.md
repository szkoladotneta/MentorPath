# MentorPath — Wdrożenie i operacje (B17)

## 1. Architektura uruchomieniowa

```
[Przeglądarka] ──HTTPS──▶ [Reverse proxy / TLS] ──HTTP──▶ [mentorpath (kontener, :8080)]
                                                            ├──▶ PostgreSQL 17 (prywatna sieć)
                                                            ├──▶ S3-compatible bucket (prywatny, brak publicznych URL)
                                                            ├──▶ SMTP (tylko reset hasła — D-11)
                                                            └──▶ Dostawca AI (opcjonalnie; domyślnie Fake)
```
Jeden obraz Docker zawiera aplikację i komendy administracyjne (`migrate`, `create-mentor`, `seed-demo`).

## 2. Konfiguracja (zmienne środowiskowe)

Sekrety wyłącznie przez zmienne środowiskowe / secret store. W repozytorium nie ma sekretów produkcyjnych (wartości w `docker-compose.yml` i `appsettings.Development.json` są lokalne).

| Zmienna | Wymagana | Opis |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | tak | `Production` |
| `ConnectionStrings__Default` | tak | Npgsql connection string (SSL zalecany: `SSL Mode=Require`) |
| `App__PublicBaseUrl` | tak | Publiczny adres HTTPS (linki zaproszeń i ustawienia hasła) |
| `App__BehindProxy` | zalecane | `true` za reverse proxy (X-Forwarded-For/Proto) |
| `App__DisableHttpsRedirection` | opcjonalne | `true`, jeśli TLS kończy się na proxy i proxy samo przekierowuje |
| `DataProtection__KeysPath` | tak | Trwały katalog kluczy (wolumen), domyślnie w obrazie `/keys` — bez niego wylogowanie po restarcie |
| `Storage__S3__ServiceUrl` | tak | Endpoint S3 (np. `https://s3.eu-central-1.amazonaws.com` lub kompatybilny) |
| `Storage__S3__BucketName` | tak | Prywatny bucket (domyślnie `mentorpath`) |
| `Storage__S3__AccessKey`, `Storage__S3__SecretKey` | tak | Poświadczenia z uprawnieniami tylko do tego bucketu (Get/Put/Delete) |
| `Storage__S3__Region`, `Storage__S3__ForcePathStyle` | opcjonalne | Region (domyślnie `us-east-1`), path-style (domyślnie `true`) |
| `Storage__EnsureBucketOnStartup` | opcjonalne | `false`, jeśli bucket zakłada infrastruktura (zalecane w produkcji) |
| `Email__Smtp__Host`, `__Port`, `__User`, `__Password`, `__EnableSsl`, `__From` | tak | SMTP dla resetu hasła |
| `Ai__Provider` | tak | `Fake` (domyślnie) lub `OpenAiCompatible` |
| `Ai__BaseUrl`, `Ai__Model`, `Ai__ApiKey` | gdy `OpenAiCompatible` | Endpoint zgodny z OpenAI Chat Completions + structured output |
| `Ai__TimeoutSeconds`, `Ai__MaxOutputTokens`, `Ai__MonthlyGenerationLimitPerWorkspace` | opcjonalne | 60 / 3000 / 50 |
| `Database__MigrateOnStartup` | opcjonalne | Nie zalecane w produkcji — używaj kroku `migrate` |

Bez `Storage__S3__ServiceUrl` aplikacja używa pamięci operacyjnej (tylko testy/dev — **nie w produkcji**).

## 3. Procedura wdrożenia

```bash
# 1. Build obrazu (CI robi to na każdym push do main)
docker build -t registry.example.com/mentorpath:<wersja> .

# 2. Backup bazy przed migracją (sekcja 5)

# 3. Migracje (jednorazowy kontener, ten sam obraz i konfiguracja)
docker run --rm --env-file prod.env registry.example.com/mentorpath:<wersja> migrate

# 4. Start / rolling restart aplikacji
docker run -d --name mentorpath --env-file prod.env -v mentorpath-keys:/keys -p 8080:8080 registry.example.com/mentorpath:<wersja>

# 5. Weryfikacja
curl -fsS https://mentorpath.example.com/health/ready
```

Sondy: `GET /health/live` (proces), `GET /health/ready` (proces + baza).

Blazor Interactive Server używa WebSocketów — reverse proxy musi przepuszczać `Upgrade`/`Connection` i mieć sticky sessions przy więcej niż jednej instancji (MVP: jedna instancja).

## 4. Operacje administracyjne

```bash
# Utworzenie mentora + workspace (wypisuje jednorazowy link ustawienia hasła — przekaż bezpiecznym kanałem)
docker run --rm --env-file prod.env registry.example.com/mentorpath:<wersja> create-mentor \
  --email anna@firma.pl --name "Anna Nowak" --workspace "Mentoring Anny"
```
Reset hasła mentee/mentora: przez stronę „Nie pamiętasz hasła?” (e-mail SMTP). Zaproszenia mentees tworzy mentor w UI i sam przekazuje link (ważny 7 dni, jednorazowy).

## 5. Backup i odtwarzanie

PostgreSQL (dane produktowe i Identity):
```bash
# codziennie (cron / scheduler), retencja np. 14 dni dziennych + 8 tygodniowych
pg_dump --format=custom --no-owner --dbname "$PG_URL" --file mentorpath-$(date +%F).dump
# weryfikacja kopii (co najmniej raz w miesiącu odtworzenie na środowisku testowym)
pg_restore --list mentorpath-YYYY-MM-DD.dump > /dev/null
# odtworzenie
createdb mentorpath_restore
pg_restore --no-owner --dbname "postgres://.../mentorpath_restore" mentorpath-YYYY-MM-DD.dump
```
Zalecane w chmurze: zarządzany PostgreSQL z PITR (point-in-time recovery) + powyższy logiczny dump jako druga linia.

Storage S3: włącz wersjonowanie bucketu i replikację/backup zgodnie z polityką dostawcy. Klucze obiektów są losowe (`ws/<workspace>/sub/<guid>`) i powiązane z `SubmissionAttachments.ObjectKey`.

Klucze Data Protection (`/keys`): backup razem z konfiguracją; utrata = wylogowanie użytkowników i unieważnienie linków resetu hasła (dane nie giną).

Lokalnie (docker compose):
```bash
docker compose exec postgres pg_dump -U mentorpath -Fc mentorpath > backup.dump
docker compose exec -T postgres pg_restore -U mentorpath -d mentorpath --clean < backup.dump
```

## 6. Monitoring i logi

- Logi: JSON na stdout (Production). Nie zawierają e-maili, tokenów, treści rozwiązań ani promptów AI.
- AI: każde wywołanie w `AiGenerationRequests` (status, provider, model, tokeny, kod błędu) — podstawa kontroli kosztów.
- Alerty: `/health/ready` ≠ 200, błędy 5xx, wzrost `AiGenerationRequests.Status = Failed`.

## 7. Checklista gotowości wydania

- [ ] CI zielone (build, unit, integration, obraz).
- [ ] `migrate` wykonane po backupie.
- [ ] HTTPS na proxy, `App__PublicBaseUrl` z `https://`, `App__BehindProxy=true`.
- [ ] Bucket prywatny (brak public ACL / policy), dedykowane poświadczenia.
- [ ] Wolumen `/keys` podpięty.
- [ ] SMTP skonfigurowany i przetestowany (reset hasła).
- [ ] Decyzja o dostawcy AI (D-19) — do tego czasu `Ai__Provider=Fake` (szkice testowe) lub wyłączenie przycisku w praktyce przez limit `0`.
- [ ] Backup działa i został raz odtworzony.
- [ ] Utworzeni mentorzy (`create-mentor`), smoke test: logowanie, zaproszenie, roadmapa.
- [ ] Polityka prywatności, retencji i umowy powierzenia z dostawcami hostingu/AI/SMTP (RODO) — poza kodem, wymagane przed użyciem z prawdziwymi danymi.
