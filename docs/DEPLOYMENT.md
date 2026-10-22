# MentorPath — Wdrożenie i operacje (B17)

**Docelowe wdrożenie: Azure (D-47).** Pełna instrukcja od pustej subskrypcji do pierwszego mentora: [AZURE_DEPLOYMENT.md](AZURE_DEPLOYMENT.md). Poniżej referencja konfiguracji i operacji; komendy `docker run` to wariant ogólny, nie komendy do wykonania wewnątrz App Service.

## 1. Architektura uruchomieniowa

```
[Przeglądarka] ──HTTPS──▶ [Reverse proxy / TLS] ──HTTP──▶ [mentorpath (kontener, :8080)]
                                                            ├──▶ PostgreSQL 17 (prywatna sieć)
                                                            ├──▶ Azure Blob Storage (prywatny, brak publicznych URL)
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
| `DataProtection__BlobUri` | Azure: tak | Pełny HTTPS URI prywatnego bloba z kluczami, bez SAS; `DefaultAzureCredential`; pierwszeństwo przed `KeysPath`; kontener tworzy operator |
| `DataProtection__KeysPath` | poza Azure | Trwały katalog kluczy współdzielony z CLI (wolumen), domyślnie w obrazie `/keys` — sam katalog w obrazie nie jest trwały |
| `Storage__Provider` | domyślnie `AzureBlob` | `InMemory` wyłącznie w testach; start Production jest odrzucany |
| `Storage__AzureBlob__ServiceUri` | Azure: tak | `https://<konto>.blob.core.windows.net`, bez SAS; dostęp managed identity |
| `Storage__AzureBlob__ContainerName` | domyślnie `mentorpath` | Prywatny kontener załączników |
| `Storage__AzureBlob__ConnectionString` | lokalnie | Azurite; ma pierwszeństwo przed `ServiceUri`, nie ustawiaj w Azure |
| `Storage__EnsureContainerOnStartup` | opcjonalne | Domyślnie `true` tylko w Development; w Production kontener tworzy operator; błąd tworzenia przerywa start |
| `Email__Smtp__Host`, `__Port`, `__User`, `__Password`, `__EnableSsl`, `__From` | tak | ACS Email: `smtp.azurecomm.net`, `587`, nazwa użytkownika SMTP z ACS, wartość sekretu aplikacji Entra (Key Vault), `true` (STARTTLS), dokładny adres nadawcy zweryfikowanej domeny; konfiguracja i ostrzeżenie o wycofaniu usługi w `AZURE_DEPLOYMENT.md` §3a |
| `Ai__Provider` | tak | `Fake` (domyślnie) lub `OpenAiCompatible` |
| `Ai__BaseUrl`, `Ai__Model`, `Ai__ApiKey` | gdy `OpenAiCompatible` | Endpoint zgodny z OpenAI Chat Completions + structured output |
| `Ai__TimeoutSeconds`, `Ai__MaxOutputTokens`, `Ai__MonthlyGenerationLimitPerWorkspace` | opcjonalne | 60 / 3000 / 50 |
| `Database__MigrateOnStartup` | opcjonalne | Nie zalecane w produkcji — używaj kroku `migrate` |

Brak konfiguracji Azure Blob to błąd walidacji przy starcie; nie ma automatycznego fallbacku do pamięci. Stare klucze `Storage__S3__*` nie są obsługiwane. Przed aktualizacją istniejącej instalacji przenieś obiekty i zmień konfigurację ([migracja](AZURE_DEPLOYMENT.md#existing-s3-data)).

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
Eksport i usunięcie danych osobowych mentee (RODO, D-42) — procedura i okresy retencji w `docs/DATA_RETENTION.md`:
```bash
# Eksport do pliku (wymagane --out; plik 0600, bez nadpisywania; na stdout tylko id i liczniki)
docker run --rm --env-file prod.env -v /secure/exports:/exports registry.example.com/mentorpath:<wersja> export-user \
  --email osoba@example.com --out /exports/<id-prośby>.json
# Anonimizacja: bez --confirm tylko podgląd liczników; --confirm wykonuje
docker run --rm --env-file prod.env registry.example.com/mentorpath:<wersja> anonymize-user --user-id <guid> --confirm
```
Reset hasła mentee/mentora: przez stronę „Nie pamiętasz hasła?” (e-mail SMTP). Zaproszenia mentees tworzy mentor w UI i sam przekazuje link (ważny 7 dni, jednorazowy).

Wybór właściciela: Azure Communication Services Email przez istniejący adapter SMTP (D-48), bez zmian kodu. **Usługa zostanie wycofana 30 września 2028**, a dostępność nowych wdrożeń może się zmienić; Microsoft odradza nowe integracje. Wariant tymczasowy wymaga migracji do innego SMTP przed tą datą. Instrukcja Azure opisuje oba zasoby ACS, domenę, aplikację Entra, role, rotację sekretu i test dostarczenia. Samo managed identity App Service nie uwierzytelnia bieżącego adaptera SMTP.

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

Azure Blob: soft delete blobów i kontenerów 30 dni; wersjonowanie domyślnie wyłączone (D-47). Soft delete nie zastępuje niezależnego backupu. Procedura kopii i próba odtworzenia: `AZURE_DEPLOYMENT.md`. Klucze obiektów są losowe (`ws/<workspace>/sub/<guid>`) i powiązane z `SubmissionAttachments.ObjectKey`. Nie dodawaj reguły wygaszania bieżących załączników.

Klucze Data Protection (Azure: osobny blob; poza Azure: wolumen `/keys`): backup razem z konfiguracją; utrata = wylogowanie użytkowników i unieważnienie linków resetu hasła (dane nie giną). Web i CLI muszą używać tego samego key ring.

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
- [ ] Kontener Blob prywatny, managed identity i role ograniczone do kontenerów.
- [ ] Klucze Data Protection trwałe (`BlobUri` w Azure albo wolumen `/keys`) i wspólne z CLI.
- [ ] SMTP skonfigurowany i przetestowany (reset hasła).
- [ ] Decyzja o dostawcy AI (D-19) — do tego czasu `Ai__Provider=Fake` (szkice testowe) lub wyłączenie przycisku w praktyce przez limit `0`.
- [ ] Backup działa i został raz odtworzony.
- [ ] Utworzeni mentorzy (`create-mentor`), smoke test: logowanie, zaproszenie, roadmapa.
- [ ] Soft delete blobów/kontenerów: 30 dni; jeśli włączono wersjonowanie, reguła usuwania poprzednich wersji po 30 dniach (`DATA_RETENTION.md` §1).
- [ ] Polityka prywatności i umowy powierzenia z dostawcami hostingu/AI/SMTP (RODO) — poza kodem, wymagane przed użyciem z prawdziwymi danymi. Retencja i procedura eksportu/usunięcia: `docs/DATA_RETENTION.md`.
