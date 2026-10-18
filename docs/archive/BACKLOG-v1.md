> **ARCHIWUM (2026-10-19, D-41).** Dokument historyczny — nie jest źródłem wymagań ani reguł. Aktualne: `docs/MVP_SPEC.md`, `docs/DECISIONS.md`, `docs/DOMAIN_RULES.md`, `docs/BACKLOG_NEXT.md`.

# MentorPath — Implementation Backlog v1.1

## P0 — Wymagane do pierwszego wydania

| ID | Zadanie | Agent | Zależności | Szacunek |
|---|---|---|---|---|
| B01 | Solution, Docker Compose, konfiguracja, CI build | A1 | — | 6 h |
| B02 | Identity, role, Workspace, migracje, bootstrap mentora | A1 | B01 | 10 h |
| B03 | Autoryzacja zasobowa i izolacja workspace'ów | A1 | B02 | 10 h |
| B04 | Zaproszenia, akceptacja i onboarding mentee | A1 | B02–B03 | 8 h |
| B05 | Katalog tematów i UI mentora | A2 | B03 | 8 h |
| B06 | Edytor trzech typów ćwiczeń, quiz options, publikacja | A2 | B05 | 10 h |
| B07 | Generowanie AI draftów, walidacja, edycja i akceptacja | A2 | B06 | 12 h |
| B08 | Roadmapy, przypisywanie ćwiczeń, publikacja | A3 | B03, B06 | 10 h |
| B09 | Dashboard mentee i widok roadmapy | A3 | B08 | 7 h |
| B10 | Rozwiązywanie quizów, próby i automatyczna ocena | A3 | B09 | 8 h |
| B11 | Zadania decyzyjne, historia prób, confidence | A3 | B09 | 8 h |
| B12 | Zadania praktyczne, linki, upload i download | A3 | B03, B11 | 12 h |
| B13 | Review queue, feedback i ponowne próby | A4 | B11 | 9 h |
| B14 | Postęp, reflection i statusy tematów | A4 | B10, B13 | 6 h |
| B15 | Dashboard mentora i lista oczekujących review | A4 | B13–B14 | 6 h |
| B16 | Testy integracyjne, izolacja danych, E2E, seed demo | A5 | Funkcje P0 | 15 h |
| B17 | Deployment, backup, konfiguracja, instrukcja uruchomienia | A5 | B01, B16 | 10 h |

**Suma bazowa: około 155 godzin.**

Z buforem na integrację, poprawki UX i problemy infrastrukturalne warto planować około **180–200 godzin**. Dla jednej osoby pracującej pełnoetatowo to raczej 4–5 tygodni; 2–4 tygodnie pozostają ambitnym celem przy dobrej równoległości agentów i małej liczbie zmian zakresu.

## Szczegółowe kryteria ukończenia zadań

### B01 — Foundation

- Solution kompiluje się.
- PostgreSQL uruchamia się lokalnie.
- Aplikacja ma health endpoint.
- CI wykonuje restore, build i test.
- Konfiguracja nie zawiera sekretów.

### B02–B03 — Identity & Isolation

- Operator może utworzyć dwóch mentorów.
- Każdy ma odrębny workspace.
- Mentor nie może otworzyć zasobów drugiego.
- Mentee nie może odczytać cudzych danych.
- Testy autoryzacji są automatyczne.

### B04 — Invitations

- Zaproszenie jest jednorazowe.
- Link wygasa.
- Obsługiwane są nowe i istniejące konta.
- Akceptacja tworzy relację mentoringową.
- Nie można przyjąć zaproszenia skierowanego do niezweryfikowanego innego e-maila.

### B05–B06 — Content

- Mentor tworzy temat.
- Tworzy każdy z trzech typów ćwiczeń.
- Walidacja blokuje niekompletny quiz.
- Published exercise jest niemodyfikowalny.
- Mentor może sklonować ćwiczenie do draftu.

### B07 — AI

- Mentor generuje draft.
- Odpowiedź AI jest walidowana.
- Draft można edytować.
- Publikacja wymaga jawnej akcji.
- Błąd AI nie blokuje pracy ręcznej.
- Obowiązuje limit generowań.

### B08–B09 — Roadmap

- Mentor przypisuje opublikowane ćwiczenia.
- Publikuje roadmapę.
- Mentee widzi ją w prawidłowej kolejności.
- Mentee nie widzi nieopublikowanych treści.

### B10–B12 — Submissions

- Quiz jest oceniany automatycznie.
- Zadanie decyzyjne wymaga sześciu pól.
- Zadanie praktyczne przyjmuje link lub plik.
- Historia prób jest niezmienna.
- Double-submit nie tworzy duplikatu.
- Załącznik jest dostępny tylko uprawnionym osobom.

### B13–B15 — Feedback & Progress

- Mentor widzi tylko swoje PendingReview.
- Feedback można wystawić tylko raz dla próby.
- NeedsRevision pozwala na nową próbę.
- Accepted zalicza zadanie.
- Progress wylicza się prawidłowo.
- Reflection jest widoczna w historii.

### B16–B17 — Release

- Testy najważniejszych przepływów przechodzą.
- Istnieją dane demonstracyjne.
- Aplikacja działa w środowisku docelowym.
- Backup jest skonfigurowany.
- Instrukcja deploymentu pozwala odtworzyć środowisko.

## P1 — Dopiero po pierwszym użyciu MVP

- AI-generated topic descriptions.
- Automatyczne powiadomienia e-mail.
- Rozbudowane punktowe rubryki ocen.
- Komentarze i dyskusje pod rozwiązaniami.
- Publiczna rejestracja mentorów.
- Współdzielenie katalogów.
- Szablony roadmap.
- Integracja GitHub API.
- Wykresy kompetencji.
- AI-assisted mentor review.

## Kryterium wypuszczenia MVP

Nie uznawać MVP za ukończone, dopóki nie przejdzie cały scenariusz:

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

> Rozwój po MVP (w tym pozycje P1) jest zaplanowany w `docs/BACKLOG_NEXT.md`.

**Koniec BACKLOG.md**