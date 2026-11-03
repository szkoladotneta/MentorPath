# MentorPath — Retencja danych i procedura RODO (minimum)

Status: **żywy dokument** · decyzje: D-42 (N04, issue #4), D-47, D-56 (N03) · dotyczy danych w PostgreSQL i w Azure Blob Storage.

Dokument opisuje, jak długo przechowujemy dane osobowe i jak operator obsługuje prośbę o **dostęp/eksport** (art. 15 i 20 RODO) oraz o **usunięcie** (art. 17). Polityka prywatności (tekst dla użytkowników) i umowy powierzenia z dostawcami hostingu, SMTP i AI są poza repozytorium (`SECURITY_REVIEW.md` S8).

## 1. Okresy przechowywania

| Kategoria danych | Okres | Co potem |
|---|---|---|
| Konto, relacja mentoringowa i jej treści (onboarding, próby, pliki, feedback, refleksje) — relacja `Active`/`Paused` | przez czas trwania relacji | — |
| Jak wyżej, wszystkie relacje mentee `Closed` | 12 miesięcy od zamknięcia ostatniej relacji | `anonymize-user --confirm` |
| Uwagi do produktu (`ProductFeedback`, N03) | jak konto osoby | `anonymize-user --confirm` czyści treść uwagi, ale zostawia wiersz z identyfikatorem autora, ścieżką strony i datą (D-56, D-57); eksport zawiera uwagi osoby |
| Zgoda na MCP review i szkice review (N56) | ostatnie dowody zgody do anonimizacji; szkice do publikacji przez mentora lub anonimizacji | cofnięcie zgody blokuje MCP, ale zachowuje szkice dla mentora; anonimizacja usuwa zgody, szkice i ich oceny kryteriów (D-67/D-68) |
| Zaproszenia zaakceptowane | jak konto osoby | e-mail zastępuje `anonymize-user` |
| Zaproszenia niezaakceptowane, wygasłe | 30 dni po `ExpiresAtUtc` | usunięcie przez operatora (SQL poniżej; nie ma konta, więc nie ma czego anonimizować w CLI) |
| Kopie bazy (`pg_dump`) | 14 dziennych + 8 tygodniowych (`DEPLOYMENT.md` §5); Azure PITR: 14 dni | kopie wygasają rotacyjnie; nie edytujemy kopii |
| Usunięte bloby i kontenery (soft delete) | 30 dni | retencja Azure Blob; dotyczy także snapshotów usuniętych przez adapter |
| Poprzednie wersje blobów | domyślnie wersjonowanie wyłączone (D-47) | jeśli operator je włączy, musi dodać regułę usuwania poprzednich wersji po 30 dniach; nigdy reguły wygaszającej bieżące załączniki |
| Zdarzenia statystyczne `ActivityEvents` | brak automatycznego wygaszania w N02 | pozostają po anonimizacji; zachowują `MenteeUserId` i `MentorUserId` (D-54) |
| Logi aplikacji | zgodnie z retencją platformy logów (zalecane ≤ 30 dni) | logi nie zawierają e-maili, tokenów, treści rozwiązań ani promptów |

Czyszczenie wygasłych zaproszeń (operator, raz w miesiącu, po backupie):
```sql
DELETE FROM "Invitations" WHERE "AcceptedAtUtc" IS NULL AND "ExpiresAtUtc" < now() - interval '30 days';
```

Prośbę osoby (eksport lub usunięcie) realizujemy **w ciągu 30 dni** od jej otrzymania. Operator prowadzi rejestr próśb (data, typ, id użytkownika, data realizacji) poza aplikacją — aplikacja nie ma tabeli audytu (N02 doda zdarzenia).

## 2. Kto jest objęty procedurą

Wyłącznie **mentees**. Konto będące właścicielem workspace'u (mentor) jest odrzucane (`privacy.unsupported_subject`) — usunięcie mentora dotyka relacji i treści innych osób i wymaga osobnej decyzji. Konto mentora, który jest równocześnie mentee w innym workspace (D-27), też jest odrzucane.

## 3. Eksport danych (`export-user`)

```bash
docker run --rm --env-file prod.env -v /secure/exports:/exports registry.example.com/mentorpath:<wersja> \
  export-user --email osoba@example.com --out /exports/<id-prośby>.json
# albo: --user-id <guid>
```

- JSON trafia **tylko do pliku** (`--out` wymagane); istniejący plik nie jest nadpisywany; uprawnienia `0600`. Na stdout jest tylko id użytkownika i liczniki — stdout jest strumieniem logów.
- Zawartość (`schemaVersion: 3`, daty UTC ISO-8601, stała kolejność): profil (id, e-mail, nazwa, data utworzenia, role), relacje jako mentee (workspace, cel, poziom, godziny, wyzwania, status, daty), wszystkie próby (ćwiczenie, temat, numer próby, status, wybrana odpowiedź quizu, odpowiedź ADR, uzasadnienie, link, confidence, data), metadane załączników (klucz obiektu, nazwa, typ, rozmiar — **bez treści plików**), feedback mentora (werdykt, trzy pola, data), refleksje, zaproszenia na e-mail osoby i uwagi do produktu wraz ze ścieżką strony.
- `reviewConsents` zawiera ostatnie dowody zgód (także cofniętych), wersję ujawnienia, daty i wersję zapisu. `reviewDrafts` zawiera nieopublikowane, także częściowe szkice dotyczące osoby oraz oceny kryteriów z nazwami, pozycjami i komentarzami. Obie kolekcje są zawsze wypełnione, także jako `[]`; `null`/brak pola oznacza starszy, nieuzupełniony eksport. To wyłącznie eksport operatora — zwykły widok mentee nie ujawnia szkiców.
- Nie zawiera: haseł, tokenów i sekretów Identity, prywatnych notatek mentora (`MentorPrivateNotes`), przewodników mentora i poprawnych odpowiedzi quizów, danych innych osób (w tym e-maila mentora).
- Pliki załączników operator może dołączyć osobno, pobierając obiekty po kluczach z eksportu.
- Przekaż plik osobie bezpiecznym kanałem i usuń go z wolumenu po przekazaniu.

## 4. Usunięcie danych (`anonymize-user`)

```bash
# 1) podgląd (nic nie zmienia, wypisuje liczniki)
docker run --rm --env-file prod.env registry.example.com/mentorpath:<wersja> anonymize-user --email osoba@example.com
# 2) wykonanie
docker run --rm --env-file prod.env registry.example.com/mentorpath:<wersja> anonymize-user --user-id <guid> --confirm
```

Kolejność: najpierw usuwane są pliki z magazynu, potem w jednej transakcji zmiany w bazie. Błąd magazynu (`privacy.storage_failed`) zostawia bazę bez zmian — uruchom polecenie ponownie. Polecenie jest bezpieczne do ponownego uruchomienia: konto anonimizuje raz, a treści czyści i pliki usuwa zawsze (np. feedback wystawiony do oczekującej próby już po anonimizacji). Zapisz w rejestrze próśb wypisany `UserId` — po anonimizacji e-mail już nie zadziała.

### Co jest usuwane

| Dane | Efekt |
|---|---|
| Konto | nazwa `Usunięty użytkownik`; e-mail i login `deleted-<id>@anonymized.invalid`; hasło, telefon, role, logowania zewnętrzne, claims i tokeny usunięte; nowy security stamp (wylogowanie); blokada konta bez końca |
| Relacje mentoringowe | status `Closed`; cel, wyzwania i prywatne notatki mentora wyczyszczone |
| Próby | odpowiedź ADR, uzasadnienie i link wyczyszczone |
| Załączniki | obiekty usunięte z magazynu, wiersze `SubmissionAttachments` usunięte |
| Feedback mentora | trzy pola tekstowe wyczyszczone |
| Zgody MCP review, szkice i oceny kryteriów szkiców | wiersze usunięte, także przy ponownej anonimizacji; historyczne liczniki raportu nie liczą tych wierszy |
| Refleksje | wniosek wyczyszczony |
| Zaproszenia na e-mail osoby | e-mail zastąpiony identyfikatorem zastępczym |
| Uwagi do produktu | treść wyczyszczona; autor id, ścieżka strony i data zachowane (D-56, D-57) |

### Co zostaje i dlaczego

Statystyki workspace'u i postęp roadmapy: identyfikatory, statusy prób, numery prób, wybrana opcja quizu, confidence przed/po, werdykty, daty, poziom i liczba godzin z onboardingu. Zdarzenia `ActivityEvents` pozostają wraz z `MenteeUserId` i `MentorUserId`, aby zachować statystyki pętli N02; nie zawierają treści odpowiedzi ani feedbacku. Te identyfikatory nadal łączą zdarzenia z kontem zanonimizowanym i są przechowywane świadomie (D-54). Oczekująca próba zostaje w kolejce mentora z pustą treścią i nazwą `Usunięty użytkownik`. Mentor może technicznie przywrócić relację `Closed → Active` — dla zanonimizowanego konta nie ma to skutków (konto zablokowane, brak danych osobowych).

### Ograniczenia

- **Kopie bazy** zawierają dane do czasu ich rotacji (§1). Przy odtwarzaniu z kopii wykonanej przed anonimizacją uruchom `anonymize-user --user-id <guid> --confirm` ponownie dla wszystkich id z rejestru próśb.
- **Azure Blob**: adapter usuwa bieżący blob i jego snapshoty. Soft delete zachowuje możliwość odtworzenia przez 30 dni (§1); po tym czasie dane wygasają. Wersjonowanie jest domyślnie wyłączone. Jeśli je włączysz, samo usunięcie bloba nie usuwa poprzednich wersji — wymagana osobna polityka 30 dni.
- Po odtworzeniu blobów lub bazy sprzed anonimizacji ponów usuwanie dla id z rejestru próśb. Klucze Data Protection w osobnym kontenerze nie podlegają retencji załączników.
