# Interaktywna platforma mentoringowa dla architektury backendu .NET — analiza rynku, MVP i plan rozwoju

*Stan badania: 17 października 2026 r.*

## Wnioski strategiczne

Pomysł ma sens, ale **nie jako kolejna platforma kursowa z checklistą tematów**. W sieci istnieją już bardzo dobre produkty rozwiązujące osobno niemal każdy element Twojej koncepcji: roadmapy technologiczne, zadania programistyczne, automatyczne sprawdzanie kodu, projekty wykonywane lokalnie, mentoring 1:1, indywidualne plany nauki, quizy, upload plików i ocenianie według rubric. Nie znalazłem jednak w przebadanym zestawie produktów rozwiązania, które łączyłoby te elementy wokół jednego bardzo konkretnego problemu:

> **„Naucz mnie podejmować dobre decyzje architektoniczne w backendzie .NET, dając mi praktykę, feedback od mojego mentora i kolejne zadania dostosowane do tego, czego jeszcze nie umiem.”**

To jest znacznie ciekawsza propozycja niż „platforma do nauki architektury”.

roadmap.sh daje krok po kroku roadmapę ASP.NET Core i agreguje materiały, projekty i zasoby, ale nie tworzy relacji mentor–mentee ani indywidualnej pętli zadanie → decyzja → feedback → ponowna próba. citeturn17view0 Exercism ma dziś w ścieżce C# 178 ćwiczeń obejmujących 62 koncepty, automatyczną analizę kodu oraz mentoring człowieka, ale jego ciężar leży przede wszystkim na znajomości C# i idiomatycznym kodzie. citeturn15search9turn15search22 CodeCrafters każe programiście budować prawdziwe systemy, takie jak Redis, Kafka czy serwer HTTP, pracować we własnym IDE i robić `git push`, po którym otrzymuje wyniki testów i podpowiedzi. citeturn16view0 MentorCruise z kolei oferuje mentoring 1:1, rozmowy, check-iny, feedback i budowanie spersonalizowanej roadmapy. citeturn16view1

Co ciekawe, od strony czysto funkcjonalnej **najbliżej Twojego opisu jest… Moodle**. Moodle pozwala tworzyć competency frameworks i learning plans przypisywane indywidualnym osobom, pokazywać progres, prosić o review kompetencji oraz dodawać dowody wcześniejszej nauki jako pliki lub URL-e. citeturn14search2 Jego moduł Assignment przyjmuje tekst i pliki, pozwala mentorowi udzielać feedbacku i oceniać według rubric, a Quiz obsługuje zarówno proste testy, jak i bardziej złożone formy samooceny z feedbackiem. citeturn13search2turn13search3

To prowadzi do najważniejszego wniosku:

**wartością produktu nie będą konta, quizy, roadmapa ani upload plików. Wszystkie te rzeczy są commodity.**

Wartością może być:

**model uczenia podejmowania decyzji architektonicznych + wysokiej jakości biblioteka scenariuszy + wiedza mentora + historia tego, jak konkretna osoba podejmuje decyzje i jak się w tym rozwija.**

To jest również potencjalny moat produktu. UI da się skopiować. Listę tematów da się wygenerować przez AI. Zadania CRUD można wygenerować w kilka minut. Znacznie trudniej skopiować kilkaset dobrych scenariuszy typu:

> „Masz aplikację używaną przez 300 klientów B2B. Jeden moduł zaczyna generować 70% obciążenia. Zespół ma cztery osoby. Czy wydzielasz mikroserwis? Uzasadnij. Jakie informacje musiałbyś dostać, żeby zmienić decyzję?”

i rubrykę, według której mentor potrafi ocenić **jakość procesu decyzyjnego**, a nie tylko to, czy odpowiedź brzmi „microservices”.

Moja rekomendacja brzmi więc:

> **Nie buduj LMS-a dla .NET developerów. Zbuduj workspace do treningu decyzji architektonicznych pomiędzy mentorem a mentee.**

## Co już istnieje

Rynek warto rozłożyć na kilka kategorii, bo każda zawiera element, który można świadomie zaadaptować.

| Produkt | Co już rozwiązuje | Czego brakuje względem Twojej wizji | Co warto „ukraść” jako wzorzec |
|---|---|---|---|
| **roadmap.sh** | Wizualne roadmapy, w tym ASP.NET Core, zasoby, projekty i ścieżki rozwoju. citeturn17view0 | Indywidualnej oceny kompetencji, review mentora, historii decyzji i dostosowanych ćwiczeń | Wizualizacja mapy i zależności pomiędzy tematami |
| **Exercism C#** | 178 ćwiczeń, 62 koncepty, analiza kodu i opcjonalny feedback mentorów. citeturn15search9 | Nacisku na architekturę systemów, wymagania niefunkcjonalne i decyzje biznesowo-techniczne | Pętla solve → submit → feedback → improve |
| **CodeCrafters** | Budowanie prawdziwych systemów we własnym IDE; Git push uruchamia testy i daje szybki feedback. citeturn16view0 | Indywidualnej roadmapy oraz stałego mentora analizującego sposób myślenia | Nie budować IDE w przeglądarce; pozwolić pracować w normalnym środowisku |
| **MentorCruise** | Mentor 1:1, personal chats, calls, check-iny, feedback i spersonalizowana roadmapa. citeturn16view1 | Głębokiej, ustrukturyzowanej warstwy edukacyjnej skoncentrowanej na architekturze .NET | Relacja długoterminowa i roadmapa należąca do konkretnej osoby |
| **Moodle** | Indywidualne learning plans, competencies, evidence, uploady, zadania, rubric, quizy i śledzenie postępu. citeturn14search2turn13search2turn13search3 | Specjalizacji w software architecture oraz workflow zaprojektowanego wokół code/architecture review | Model competencies + evidence + review |
| **Microsoft Learn / dokumentacja .NET** | Oficjalne materiały dotyczące ASP.NET Core, .NET i budowania backendów; Microsoft publikuje także dedykowane materiały architektoniczne dotyczące monolitów i mikroserwisów. citeturn4search20turn17view3turn17view4 | Personalizacji i mentoringu 1:1 | Źródło referencyjne zamiast przepisywania dokumentacji |
| **Hyperskill** | Ścieżki dobierane do doświadczenia i zainteresowań oraz naukę opartą o projekty. citeturn3view1 | Obecny katalog backendowy nie jest wyspecjalizowany w architekturze .NET ani mentoringu 1:1 | Projekt jako nośnik wielu kompetencji, a nie osobne izolowane lekcje |

Szczególnie ważny jest CodeCrafters. Produkt świadomie nie zmusza programisty do korzystania z zabawkowego webowego edytora: użytkownik pracuje w swoim IDE, terminalu i Git, a platforma koncentruje się na organizacji wyzwania i feedbacku. citeturn16view0 To bardzo dobry kierunek dla Twojego produktu.

Nie ma więc powodu, żeby MVP zawierało własny edytor C#, kompilator czy sandbox.

Mentee może dostać:

```text
Zadanie
  ↓
git clone / własne repo
  ↓
Rider / Visual Studio / VS Code
  ↓
implementacja
  ↓
GitHub URL / PR / plik / odpowiedź tekstowa
  ↓
Twoje review
  ↓
poprawiona wersja
```

Platforma powinna zarządzać **procesem uczenia**, a nie zastępować narzędzia programisty.

Istotnym historycznym wzorcem był również GitHub Classroom: pozwalał na automatyczne uruchamianie testów po pushu i organizację feedbacku nad pracą studenta. GitHub wycofał jednak Classroom 28 sierpnia 2026 r., więc nie budowałbym dziś produktu w oparciu o tę usługę. Sam wzorzec push → CI → feedback nadal jest natomiast bardzo wartościowy. citeturn10search0turn10search4

### Czy można więc zrobić to bez budowania własnej aplikacji?

Tak.

Dla jednej osoby niemal cały proces można dziś odtworzyć w Moodle:

**Learning Plan → Competencies → Assignment → Submission → Rubric → Review → Quiz → kolejna kompetencja.** Moodle pozwala nawet użytkownikowi dodać evidence w postaci pliku lub URL-a i powiązać je z konkretną kompetencją. citeturn14search2

To jest bardzo ważne biznesowo.

Oznacza bowiem, że **techniczna możliwość stworzenia aplikacji nie jest hipotezą wartą testowania**. Ona jest oczywista i rozwiązana.

Hipotezą do przetestowania jest:

> Czy developer zapłaci za ustrukturyzowany proces, który rzeczywiście poprawia jego zdolność podejmowania decyzji architektonicznych?

I drugą:

> Czy taki system pozwala Tobie prowadzić jednocześnie większą liczbę osób bez proporcjonalnego zwiększania czasu mentoringu?

Dopiero gdy odpowiedzi są pozytywne, customowa platforma daje dużą przewagę.

## Gdzie jest realna luka produktowa

W przebadanych rozwiązaniach nie znalazłem jednego produktu, który jednocześnie posiadałby wszystkie poniższe elementy:

**specjalizacja backend architecture w .NET → diagnoza konkretnej osoby → indywidualny graf kompetencji → rzeczywiste problemy architektoniczne → oddawanie artefaktów → review stałego mentora → rejestrowanie zmian sposobu myślenia → automatyczne dostosowywanie kolejnych wyzwań.**

To jest wniosek z porównania roadmap.sh, Exercism, CodeCrafters, MentorCruise i Moodle, a nie stwierdzenie, że żaden niszowy produkt na świecie nigdy tego nie implementował. citeturn17view0turn15search9turn16view0turn16view1turn14search2

I właśnie tutaj widzę ciekawą zmianę perspektywy.

Typowa platforma edukacyjna przechowuje:

```text
user -> course -> lesson -> quiz -> score
```

Twoja mogłaby przechowywać:

```text
mentee
   ↓
goal
   ↓
competency graph
   ↓
decision challenge
   ↓
decision + reasoning + confidence
   ↓
mentor assessment
   ↓
feedback
   ↓
second attempt
   ↓
evidence of mastery
   ↓
next challenge
```

To fundamentalnie inny model produktu.

### Roadmapa nie powinna być checklistą

roadmap.sh świetnie pokazuje, jak wiele tematów może wejść w ścieżkę ASP.NET Core, ale jego podstawową rolą jest wskazanie kierunku nauki. citeturn17view0 W mentoringu personalnym bardziej użyteczny byłby **graf kompetencji z prerequisite'ami**.

Przykładowo:

```text
HTTP
 ├── API design
 │    ├── idempotency
 │    └── versioning
 │
Transactions
 ├── concurrency
 ├── idempotency
 └── Outbox
      └── eventual consistency

Modularity
 ├── boundaries
 ├── modular monolith
 └── microservices

Observability
 ├── logging
 ├── metrics
 └── distributed tracing
```

Dwóch mentees może więc mieć ten sam katalog kompetencji, ale zupełnie inną ścieżkę.

Developer A:

```text
EF Core
→ Transactions
→ Concurrency
→ Messaging
→ Outbox
```

Developer B:

```text
HTTP
→ API design
→ Authentication
→ Authorization
→ Threat modeling
```

Nie potrzebujesz zatem „roadmapy dla każdego użytkownika” jako osobnej struktury treści. Potrzebujesz **jednego katalogu kompetencji i instancji Learning Plan**, która wybiera z niego odpowiednie węzły, kolejność i priorytety.

### Najbardziej wartościowym obiektem może być nie Topic, lecz Decision

Najciekawszym elementem domenowym nie jest:

```csharp
Topic
{
    Id
    Name
    Description
}
```

lecz coś w rodzaju:

```text
ArchitectureChallenge

Context
Constraints
Unknowns
Question
PossibleAlternatives
EvaluationRubric
FollowUpQuestions
Difficulty
RelatedCompetencies
```

Przykład:

> System realizuje około 20 zamówień na sekundę. Zespół liczy pięć osób. Moduł płatności musi być bardziej niezawodny niż pozostała część aplikacji. CTO proponuje rozbicie obecnego modularnego monolitu na mikroserwisy.
>
> Co robisz?

Nie powinno być czterech odpowiedzi:

```text
A. microservices
B. event sourcing
C. CQRS
D. clean architecture
```

Mentee powinien napisać:

1. jak rozumie problem,
2. których informacji mu brakuje,
3. jakie widzi opcje,
4. którą wybiera,
5. jakie są koszty wyboru,
6. kiedy zmieniłby decyzję.

To jest dokładnie ta umiejętność, która później przenosi się na prawdziwe projekty.

Microsoft we własnym przewodniku architektonicznym podkreśla analizę opcji i „decision considerations”; ten sam materiał zauważa również, że w ogromnej części przypadków pojedyncza aplikacja jest prostsza do budowania, wdrażania i debugowania niż zestaw usług, a mimo to może spełniać wymagania biznesowe. citeturn17view3 To świetny materiał na zadania, w których uczestnik nie ma zgadywać „jaki pattern lubi mentor”, lecz ocenić kontekst.

Równie ciekawe jest ostrzeżenie Microsoftu dotyczące referencyjnego eShopOnContainers: aplikacja służy do demonstrowania wzorców i **nie jest produkcyjnym template'em**, który należy kopiować do prawdziwych systemów. citeturn17view4

To właściwie idealnie streszcza filozofię Twojego programu:

> **architektury nie uczymy przez kopiowanie diagramów — uczymy przez rozumienie, kiedy dany kompromis jest uzasadniony.**

## Jak zaprojektować naukę, a nie tylko katalog treści

Badania edukacyjne dobrze wspierają odejście od modelu „obejrzyj materiał → kliknij Complete”. Meta-analiza 225 badań dotyczących edukacji STEM wykazała przewagę aktywnego uczenia nad tradycyjnym modelem wykładowym. citeturn7search0 Z kolei eksperymenty Butlera pokazały, że powtarzane odtwarzanie wiedzy przez testowanie poprawiało nie tylko retencję, ale również późniejszy transfer wiedzy do nowych pytań inferencyjnych, w porównaniu z ponownym studiowaniem materiału. citeturn19view1

Nie oznacza to automatycznie, że „quiz = świetny architekt”. Wniosek projektowy jest węższy: **użytkownik powinien regularnie coś z wiedzą robić**, a nie jedynie ją konsumować.

Dlatego proponowałbym, żeby około 70–80% doświadczenia w aplikacji kręciło się wokół aktywności, a dokumentacja i materiały były przede wszystkim wsparciem. Ten udział jest propozycją projektową, nie wynikiem cytowanych badań.

### Typy ćwiczeń

Biblioteka ćwiczeń powinna być dużo szersza niż „napisz kod”.

| Typ | Przykład | Co naprawdę testujesz |
|---|---|---|
| **Decision scenario** | „Monolit czy mikroserwis?” przy konkretnych ograniczeniach | Trade-off thinking |
| **Architecture review** | Dostaje diagram systemu i ma znaleźć trzy ryzyka | Rozpoznawanie problemów |
| **Code review** | Dostaje fragment .NET i ma wskazać problem granic/transakcji | Przełożenie architektury na kod |
| **Refactoring task** | Rozdzielenie modułu bez zmiany zachowania systemu | Modularyzację |
| **Failure scenario** | „Po retry klient został obciążony dwa razy” | Idempotency, reliability |
| **Incident analysis** | API nagle ma p95 = 4 s | Diagnostykę i observability |
| **ADR** | Podjęcie decyzji i zapisanie jej razem z alternatywami | Komunikację architektoniczną |
| **Implementation** | Zaimplementowanie Outboxa lub optimistic concurrency | Umiejętność wykonania rozwiązania |
| **Prediction** | „Co wydarzy się przy dwóch równoległych requestach?” | Mental model |
| **Teach-back** | „Wytłumacz juniorowi dlaczego nie użyliśmy microservices” | Głębokość zrozumienia |
| **Adversarial question** | „Czy retry zawsze zwiększa niezawodność?” | Wychwytywanie uproszczeń |
| **Quiz** | Krótkie pytania po czasie od poprzedniego tematu | Retrieval i transfer |

Dobry przykład „podchwytliwego” pytania:

> Masz endpoint:
>
> `POST /orders`
>
> Klient dostał timeout, ale serwer zdążył utworzyć zamówienie. Klient wykonuje retry.
>
> **Gdzie jest problem?**
>
> A następnie:
>
> **Podaj co najmniej trzy rozwiązania i dla każdego przypadek, w którym go nie wybierzesz.**

Pierwsza odpowiedź sprawdza wiedzę.

Druga sprawdza architekturę.

### Nie oceniaj tylko odpowiedzi — oceniaj proces decyzyjny

Proponowałbym jedną wspólną rubric dla dużej części zadań:

| Kryterium | Słabo | Dobrze | Bardzo dobrze |
|---|---|---|---|
| **Rozpoznanie ograniczeń** | Od razu proponuje technologię | Uwzględnia najważniejsze constraints | Aktywnie identyfikuje brakujące informacje |
| **Alternatywy** | Jedno rozwiązanie | Kilka realnych opcji | Potrafi również odrzucić pozornie atrakcyjne opcje |
| **Trade-offs** | „X jest lepsze” | Pokazuje plusy i minusy | Łączy koszt z konkretnymi wymaganiami |
| **Ewolucja rozwiązania** | Projektuje „na zawsze” | Rozumie przyszłe zmiany | Wybiera rozwiązanie łatwe do zmiany i określa trigger zmiany |
| **Operacyjność** | Patrzy tylko na kod | Uwzględnia deployment/monitoring | Analizuje failure modes i operacje |
| **Komunikacja** | Pattern dumping | Jasno uzasadnia decyzję | Potrafi przekonać technicznie i biznesowo |

W efekcie zamiast:

```text
Microservices: 7/10
```

masz:

```text
Architecture challenge: Payment extraction

Constraints recognition   █████████░  4/5
Alternatives              ██████░░░░  3/5
Trade-offs                ████████░░  4/5
Failure modes             ████░░░░░░  2/5
Evolution                 ████████░░  4/5
```

I tutaj zaczyna powstawać coś dużo ciekawszego niż LMS.

Po sześciu miesiącach możesz powiedzieć:

> „Zobacz. Kiedy zaczynaliśmy, praktycznie zawsze skakałaś bezpośrednio do rozwiązania. Teraz w czterech ostatnich zadaniach zaczynałaś od constraints i alternatives.”

To jest **widoczny dowód rozwoju sposobu myślenia**.

### Mierzyłbym również pewność odpowiedzi

Po podjęciu decyzji można pytać:

> Jak bardzo jesteś pewna swojej decyzji?
>
> `0 — 100%`

Nie traktowałbym tego jako oceny. To eksperymentalna metryka produktu.

Może się okazać, że znacznie ważniejsza od wzrostu surowego score jest zmiana:

```text
wcześniej:
confidence 95%
mentor score 2/5

później:
confidence 70%
mentor score 4/5
```

Architekt, który zaczyna dostrzegać niepewność i potrzebę dodatkowych informacji, może podejmować dojrzalsze decyzje niż osoba bezrefleksyjnie pewna każdej odpowiedzi. To jest moja hipoteza produktowa, którą warto mierzyć, a nie założenie, które należy wpisać na stałe w scoring.

### Bazowa roadmapa dla backend architecture .NET

Nie budowałbym roadmapy jako kilkuset technologii. Bazowy katalog powinien opisywać **obszary decyzji**.

Przykładowy szkielet:

| Obszar | Co mentee powinien potrafić zdecydować | Przykładowe zadanie |
|---|---|---|
| **ASP.NET Core fundamentals** | pipeline, middleware, DI lifetimes, hosting | Znajdź przyczynę błędu wynikającego z captive dependency |
| **HTTP i API design** | contracts, errors, versioning, pagination, idempotency | Zaprojektuj API składania zamówienia odporne na retry |
| **Modularity** | boundaries, coupling, cohesion, dependency direction | Podziel istniejący backend na moduły |
| **Data & consistency** | transactions, concurrency, indexing, migrations | Napraw lost update przy równoległych requestach |
| **Application architecture** | layered, vertical slices, Clean Architecture, kiedy nie stosować wzorca | Porównaj dwie struktury tej samej aplikacji |
| **Async processing** | queues, Outbox, retries, DLQ, eventual consistency | Zaprojektuj wysyłanie powiadomień po zamówieniu |
| **Testing** | unit/integration/e2e i dobór testu do ryzyka | Zaprojektuj strategię testowania payment flow |
| **Reliability** | timeout, retry, circuit breaking, graceful degradation | Zdiagnozuj cascading failure |
| **Observability** | logs, metrics, traces, correlation | Dostajesz incydent bez stack trace — czego brakuje? |
| **Security** | authentication, authorization, secrets, boundaries | Znajdź problemy bezpieczeństwa API |
| **Performance** | caching, DB bottlenecks, scaling | p95 wzrosło z 150 ms do 2 s — zaproponuj kolejność diagnostyki |
| **Distributed systems** | kiedy dystrybuować, consistency, communication | Uzasadnij wydzielenie lub niewydzielenie mikroserwisu |
| **Architecture evolution** | reversibility, ADR, technical debt | Napisz ADR i warunki ponownego rozpatrzenia decyzji |

Jako aktualną bazę technologiczną przy tworzeniu materiałów wybrałbym **.NET 10 LTS**. Według aktualnej dokumentacji Microsoftu jest to obecnie wspierana wersja LTS z supportem do listopada 2028 r.; .NET 8 i .NET 9 kończą support w listopadzie 2026 r. citeturn17view1turn17view2

Natomiast sam katalog kompetencji powinien możliwie długo pozostać niezależny od wersji:

```text
Competency:
"Understand optimistic concurrency"
```

a dopiero zasób lub ćwiczenie może mieć:

```text
Technology:
EF Core 10

Framework:
.NET 10
```

Dzięki temu premiera .NET 11 czy .NET 12 nie rozwala modelu domenowego Twojej aplikacji.

## MVP, które naprawdę warto zbudować

Najważniejsza decyzja brzmi:

> **MVP nie powinno automatyzować mentoringu. Powinno sprawdzić, czy aplikacja wzmacnia mentoring.**

Dlatego nie zaczynałbym od AI generującego personalną roadmapę.

Ty jesteś dziś algorytmem personalizującym roadmapę.

To przewaga, nie problem.

### MVP w jednym zdaniu

**Mentor tworzy dla konkretnej osoby spersonalizowaną ścieżkę kompetencji, przypisuje jej praktyczne wyzwania, mentee oddaje rozwiązania, mentor dokonuje ustrukturyzowanego review, a system pokazuje kolejne zadanie i historię rozwoju.**

To wystarczy.

### Widok mentee

Po zalogowaniu użytkownik powinien przede wszystkim widzieć:

```text
Cześć, Ania 👋

Twój cel:
"Samodzielnie projektować backendy średniej wielkości"

────────────────────────────────

Teraz skupiamy się na:
Data consistency

○ Transactions
● Concurrency
○ Idempotency
🔒 Outbox
🔒 Eventual consistency

────────────────────────────────

NEXT ACTION

Lost Update
Architecture Challenge

~30–45 min
[ Start ]

────────────────────────────────

Ostatni feedback
"Rozwiązanie jest poprawne technicznie,
ale za szybko wybrałaś pessimistic locking..."

[ Open review ]
```

Nie zaczynałbym od ogromnego dashboardu.

Najważniejszym elementem interfejsu jest:

> **„Co powinienem zrobić teraz?”**

Roadmapa jest kontekstem.

Next action jest produktem.

### Widok tematu

Przykładowo:

```text
Optimistic Concurrency
────────────────────────────

WHY IT MATTERS

Dwa requesty mogą poprawnie przeczytać
ten sam stan i nadpisać swoje zmiany.

YOU SHOULD BE ABLE TO

✓ rozpoznać lost update
✓ zdecydować optimistic vs pessimistic
✓ obsłużyć konflikt w EF Core
✓ wyjaśnić konsekwencje użytkownikowi

RESOURCES

Microsoft Docs
Article selected by mentor
Your notes

EXERCISES

✓ Spot the bug
● Architecture scenario
○ Implement solution
○ Reflection

MENTOR ASSESSMENT

Practicing
```

### Typy zadań w MVP

Nie potrzebujesz dziesięciu rendererów.

Wystarczą trzy.

**Question / Scenario**

```text
Markdown problem
↓
text answer
↓
mentor review
```

**Quiz**

```text
question
↓
choice / short answer
↓
instant explanation
```

**External exercise**

```text
instructions
↓
user works outside app
↓
submission:
- URL
- file
- text
↓
mentor review
```

To obsłuży prawdopodobnie ponad 90% materiałów, które będziesz chciał tworzyć na początku; wartość ta jest moim założeniem produktowym do zweryfikowania na realnych zadaniach, a nie danymi zewnętrznymi.

Model Moodle potwierdza, że tekst, pliki, rubric i feedback wystarczają do obsłużenia bardzo szerokiej klasy assignmentów, a CodeCrafters pokazuje, że zaawansowane zadanie programistyczne może być świadomie wykonywane poza samą platformą. citeturn13search2turn16view0

### Flow mentora

Twój workflow jest równie ważny jak UX mentee.

```text
New mentee
     ↓
diagnostic interview
     ↓
select target
     ↓
clone roadmap template
     ↓
disable / reorder topics
     ↓
assign first challenge
     ↓
submission arrives
     ↓
review using rubric
     ↓
choose:
  ├── Retry
  ├── Follow-up
  ├── Mastered
  └── Needs prerequisite
     ↓
next challenge
```

Ta ostatnia decyzja jest istotniejsza niż automatyczny procent ukończenia kursu.

Mentee może zaliczyć quiz z messagingu na 100%, a podczas scenario pokazać, że nie rozumie idempotency.

Wtedy mentor mówi:

```text
Messaging:
Knowledge       ✓
Implementation  ✓
Decision making ⚠
```

i ścieżka się zmienia.

### Status kompetencji

Zamiast:

```text
0%
25%
50%
75%
100%
```

wybrałbym stany bardziej odpowiadające rzeczywistemu mentoringowi:

```text
Not assessed
↓
Learning
↓
Practicing
↓
Demonstrated
↓
Needs revisit
```

„Demonstrated” powinno oznaczać:

> widziałem dowód, że potrafisz tę wiedzę zastosować.

To jest bliskie modelowi competencies + evidence używanemu przez Moodle, gdzie użytkownik może przedstawiać dowody związane z konkretnymi kompetencjami i prosić o ich review. citeturn14search2

### Minimalny model domenowy

Nie komplikowałbym go bardziej niż:

```text
User
MentorProfile
MenteeProfile

Competency
CompetencyPrerequisite

RoadmapTemplate
LearningPlan
LearningPlanCompetency

Exercise
ExerciseCompetency

Assignment
Submission

Review
Rubric
RubricCriterion
RubricAssessment

Resource
```

Kluczowe rozróżnienie:

```text
Exercise
```

jest **reusable content**, natomiast:

```text
Assignment
```

jest konkretnym przypisaniem exercise do konkretnego mentee.

Dzięki temu:

```text
Exercise:
"Design reliable payment endpoint"
```

może zostać wykorzystane 50 razy, a każda osoba będzie miała osobne:

```text
Assignment
Submission
Review
```

To bardzo szybko stanie się ważne.

### Co celowo wyrzucić z MVP

Nie budowałbym jeszcze:

- AI mentora,
- automatycznie generowanej roadmapy,
- chatu realtime,
- wideorozmów,
- kalendarza,
- płatności,
- marketplace mentorów,
- forum,
- achievementów,
- streaków,
- leaderboardów,
- mobilnej aplikacji,
- własnego IDE,
- własnego runnera C#,
- microservices,
- event busa,
- rekomendacji ML.

To nie są złe funkcje. Po prostu żadna z nich nie odpowiada na pierwszą hipotezę:

> **czy workflow roadmapa → wyzwanie → decyzja → feedback → progres daje zauważalnie lepsze doświadczenie mentoringowe?**

## Roadmapa rozwoju produktu

Rozwój podzieliłbym według **ryzyk, które usuwasz**, a nie według liczby funkcji.

| Faza | Co budujesz | Jakie pytanie testujesz | Warunek przejścia dalej |
|---|---|---|---|
| **Concierge validation** | Proces na jednej osobie, nawet częściowo Moodle/Notion + repozytorium | Czy taki model uczenia daje wartość? | Mentee regularnie wykonuje zadania i feedback wpływa na kolejne decyzje |
| **Mentoring MVP** | Konto, personal roadmap, topic, 3 rodzaje ćwiczeń, submissions, review, rubric, progress | Czy własny workflow jest lepszy niż zestaw przypadkowych narzędzi? | Korzystacie z aplikacji jako głównego miejsca mentoringu |
| **Mentor OS** | Templates, prerequisites, exercise library, roadmap editor, history, filters | Czy możesz obsłużyć kolejną osobę bez kopiowania wszystkiego ręcznie? | Ponowne użycie contentu istotnie zmniejsza Twoją pracę organizacyjną |
| **Automation** | Repo integrations, CI results, reminders, rules-based progression | Co można zautomatyzować bez utraty jakości feedbacku? | Automatyzacja usuwa powtarzalną pracę mentora |
| **Adaptive learning** | Diagnostic assessment, branching, spaced revisits, confidence tracking | Czy system potrafi sugerować następne ćwiczenie? | Rekomendacja systemu regularnie pokrywa się z decyzją mentora |
| **AI augmentation** | Draft review, challenge generation, Socratic questions, plan suggestions | Gdzie AI realnie oszczędza czas, nie psując mentoringu? | Mentor poprawia niewielką część wygenerowanych sugestii |
| **Platform** | Wielu mentorów, billing, organizations, marketplace/B2B | Czy istnieje produkt niezależny od Ciebie? | Inni mentorzy osiągają wartościowe wyniki bez Twojej ręcznej obsługi |

### Concierge validation

Tutaj celowo możesz praktycznie nic nie programować.

Moodle już zapewnia learning plans, competencies, pliki, URL-e, assignments, rubric i quizy. citeturn14search2turn13search2turn13search3 Możesz więc przetestować program:

```text
Competency
↓
Scenario
↓
Submission
↓
Review
↓
Next competency
```

z obecną mentee.

W tym czasie warto rejestrować każde miejsce, w którym myślisz:

> „Kurczę, potrzebuję tu funkcji X.”

Po miesiącu takich notatek backlog customowej aplikacji będzie znacznie lepszy niż backlog wymyślony dzisiaj.

### Mentoring MVP

To jest pierwsza wersja, którą faktycznie budowałbym jako własny produkt.

Powinna mieć siedem głównych ekranów:

```text
Login
Dashboard
Roadmap
Competency
Exercise
Submission
Review
```

oraz stronę mentora:

```text
Mentees
Assignments awaiting review
Roadmap editor
Exercise editor
```

Na tym etapie personalizacja pozostaje manualna.

I dobrze.

Jeśli masz trzy osoby:

```text
Anna -> path A
Piotr -> path B
Kasia -> path C
```

to Ty zmieniasz kolejność.

Nie potrzebujesz jeszcze:

```text
PersonalizationEngineFactory
```

### Mentor OS

To może być najważniejszy etap komercyjnie.

Po kilku mentees zobaczysz:

```text
„Znowu tłumaczę Outbox”
„Znowu daję podobne pytanie o transaction boundaries”
„Znowu sprawdzam to samo w ADR”
```

Wtedy tworzysz bibliotekę:

```text
Outbox
├── Concept card
├── Quiz A
├── Scenario A
├── Scenario B
├── Code exercise
├── Failure investigation
└── Follow-up questions
```

Mentor nie tworzy już kursu.

Mentor komponuje ścieżkę z istniejących elementów.

To jest moment, w którym produkt zaczyna zwiększać Twoją skalowalność.

### Automation

Tutaj pojawia się integracja z GitHubem:

```text
Assignment
↓
repository / branch / PR
↓
CI
↓
tests
↓
result imported into platform
↓
mentor reviews architecture
```

Automat powinien sprawdzać rzeczy jednoznaczne:

```text
✓ solution compiles
✓ tests pass
✓ endpoint behaves correctly
✓ migration works
```

Ty sprawdzasz:

```text
Why this boundary?
Why this transaction?
Why this abstraction?
What happens on failure?
What would make you redesign this?
```

To ważne rozdzielenie odpowiedzialności.

**Maszyna sprawdza correctness. Mentor sprawdza judgement.**

CodeCrafters bardzo skutecznie wykorzystuje właśnie część pierwszą tego modelu: praca odbywa się lokalnie, a push uruchamia testy i zwraca szybki feedback. citeturn16view0

### Adaptive learning

Dopiero tutaj warto przejść od:

```text
mentor chooses next
```

do:

```text
system suggests next
mentor accepts / changes
```

Na początku nawet bez AI:

```text
IF concurrency < Demonstrated
AND transactions = Demonstrated
THEN suggest concurrency scenario B
```

albo:

```text
IF exercise failed twice
THEN recommend prerequisite
```

Dzięki temu zbierasz dane potrzebne później do inteligentniejszej personalizacji.

### AI augmentation

AI umieściłbym dopiero stosunkowo późno.

Nie dlatego, że jest nieprzydatne, lecz dlatego, że najpierw potrzebujesz własnego modelu:

```text
co jest dobrą odpowiedzią?
co jest typowym błędem?
jak wygląda dobry feedback?
co powinno nastąpić po tym błędzie?
```

Po kilkuset prawdziwych reviews masz znacznie lepszą bazę do funkcji typu:

```text
AI: "Mentee zaproponowała distributed transaction.
W poprzednich zadaniach miała problemy z eventual consistency.
Sugeruję dopytać:
'Czy wszystkie operacje naprawdę muszą być atomic?'"
```

AI nie zastępuje wtedy mentora.

**Podpowiada mentorowi, gdzie nacisnąć.**

To znacznie ciekawszy produkt niż chatbot „Ask AI about Clean Architecture”.

## Architektura techniczna, metryki i największe ryzyka

Sam produkt jest świetną okazją, żeby zastosować dokładnie to, czego będziesz uczył mentee: **nie rozwiązywać problemów, których jeszcze nie masz**.

### Architektura MVP

Wybrałbym:

```text
ASP.NET Core / .NET 10
        │
        ▼
 Modular Monolith
        │
  ┌─────┼───────────┐
  ▼     ▼           ▼
Learning Content   Mentoring
Plans              Workflow
  │                  │
  └────────┬─────────┘
           ▼
      Relational DB

      Object Storage
        for files
```

.NET 10 jest obecnie wspieraną wersją LTS i ma support do listopada 2028 r. citeturn17view1

Nie widzę powodu dla mikroserwisów w MVP. Co ciekawe, oficjalny przewodnik Microsoftu dotyczący nowoczesnych aplikacji ASP.NET Core mówi wprost, że pojedyncza aplikacja jest w zdecydowanej większości sytuacji prostsza do budowania, wdrażania i debugowania od systemu składającego się z wielu usług, nadal mogąc spełniać wymagania biznesowe. citeturn17view3

W praktyce:

```text
MentoringPlatform.Web
MentoringPlatform.Application
MentoringPlatform.Domain
MentoringPlatform.Infrastructure
```

albo package-by-feature:

```text
Features/
  LearningPlans/
  Competencies/
  Exercises/
  Assignments/
  Reviews/
  Users/
```

jest więcej niż wystarczające.

Nie musisz nawet na siłę robić czterech projektów .NET. Ważniejsze są wyraźne granice feature'ów niż liczba `.csproj`.

### Roadmapa nie wymaga graph database

Mimo że masz graf:

```text
Competency A
    ↓
Competency B
    ↓
Competency C
```

relacyjna baza doskonale wystarczy na start:

```text
Competencies

Id
Name
Slug
Description
```

oraz:

```text
CompetencyPrerequisites

CompetencyId
PrerequisiteCompetencyId
```

Nie wprowadzałbym Neo4j tylko dlatego, że w domenie pojawiło się słowo „graph”.

### Pliki poza bazą danych

Model:

```text
Submission
  Text
  ExternalUrl
  FileReference
```

pozwala obsłużyć:

```text
GitHub repo
Pull Request
ADR.pdf
architecture.drawio
screenshot
zip
markdown answer
```

Sam plik przechowywałbym w object storage, a w bazie jedynie jego metadane i referencję. To jest rekomendacja architektoniczna dla tego produktu, nie wymaganie wynikające z któregokolwiek z analizowanych narzędzi.

### Content jako dane, nie kod

Bardzo ważna decyzja:

nie rób:

```csharp
if (topic == Topic.Outbox)
{
    ShowQuestion(...);
}
```

Ćwiczenia od początku powinny być contentem:

```text
Exercise

Title
DescriptionMarkdown
Type
Difficulty
ExpectedDuration
MentorNotes
Status
Version
```

oraz:

```text
ExerciseCompetency

ExerciseId
CompetencyId
Weight
```

Po roku prawdopodobnie większą wartością systemu będzie baza:

```text
50 competencies
300 exercises
100 architecture scenarios
50 rubrics
1000 reviews
```

niż sam kod aplikacji.

I to jest dobra wiadomość.

### Jeden szczególnie ważny ekran mentora

Zaprojektowałbym bardzo wcześnie:

```text
INBOX

Anna
Concurrency scenario
submitted 2h ago

Piotr
Outbox implementation
submitted yesterday

Kasia
ADR: Monolith vs microservices
resubmitted
```

Po kliknięciu:

```text
QUESTION
──────────────────────

SUBMISSION
──────────────────────

PREVIOUS ATTEMPT
──────────────────────

RELATED COMPETENCIES
──────────────────────

RUBRIC
Constraints       3 / 5
Alternatives      4 / 5
Trade-offs        2 / 5
Failure modes     2 / 5

FEEDBACK
──────────────────────

NEXT:
[ Retry ]
[ Follow-up exercise ]
[ Demonstrated ]
```

Jeżeli kiedyś będziesz prowadził 20 osób, właśnie ten ekran prawdopodobnie stanie się centrum biznesu.

### Metryki, które naprawdę coś powiedzą

Nie skupiałbym się na:

```text
DAU
time in app
number of page views
```

Platforma mentoringowa nie musi być używana codziennie ani długo.

Mierzyłbym raczej:

| Metryka | Dlaczego jest ciekawa |
|---|---|
| `exercise_started → submitted` | Czy zadania są wykonalne i angażujące |
| `submitted → reviewed` | Jak szybko działa pętla mentoringowa |
| `reviewed → resubmitted` | Czy feedback prowadzi do działania |
| `assignments / demonstrated competency` | Ile praktyki wymaga temat |
| `mentor review time` | Czy produkt faktycznie skaluje mentoring |
| `reused exercises / all assignments` | Czy powstaje biblioteka wielokrotnego użycia |
| `mentor suggestion accepted` | W przyszłości: jakość adaptacyjnych rekomendacji |
| `confidence vs mentor assessment` | Eksperymentalna miara kalibracji |
| `same mistake recurrence` | Czy feedback przenosi się na kolejne sytuacje |

Najważniejszą biznesowo metryką może być:

> **ile minut Twojej pracy potrzeba, aby doprowadzić mentee do jednego nowego „Demonstrated competency”.**

Jeśli wraz z rozwojem biblioteki spada:

```text
120 min
↓
90 min
↓
55 min
↓
35 min
```

bez pogorszenia jakości mentoringu, platforma zaczyna realizować realną przewagę operacyjną.

### Największe ryzyko: zbudowanie Udemy dla jednej osoby

Technicznie bardzo łatwo dojść do:

```text
Courses
Modules
Lessons
Videos
Quizzes
Progress %
Certificates
```

I wtedy prawdopodobnie zgubisz to, co w pomyśle najciekawsze.

roadmap.sh już organizuje roadmapy. citeturn17view0 Microsoft już dostarcza ogromną ilość materiałów .NET. citeturn4search20 Exercism już ma setki ćwiczeń C#. citeturn15search9 CodeCrafters już tworzy zaawansowane projekty programistyczne. citeturn16view0 MentorCruise już łączy mentorów z mentees. citeturn16view1 Moodle już ma praktycznie pełny mechanizm LMS. citeturn14search2turn13search2turn13search3

Nie musisz być lepszy od wszystkich tych produktów w ich własnej grze.

Możesz połączyć ich najlepsze mechanizmy wokół znacznie węższego problemu:

```text
roadmap.sh
    ↓
dobry model ścieżki

Exercism
    ↓
practice + human feedback

CodeCrafters
    ↓
real work in real IDE

Moodle
    ↓
competency + evidence + review

MentorCruise
    ↓
personal mentor relationship

             +
             │
             ▼

Twoja wiedza o .NET architecture
             +
biblioteka realnych decyzji
             +
historia sposobu myślenia mentee
             │
             ▼

     Architecture Mentoring OS
```

I to prowadzi do definicji produktu, którą uznałbym za najmocniejszą:

> **Personal Architecture Gym for .NET developers**
>
> Nie miejsce, w którym developer „uczy się Clean Architecture”.
>
> Miejsce, w którym regularnie **ćwiczy podejmowanie decyzji architektonicznych**, dostaje feedback od bardziej doświadczonego inżyniera i z czasem buduje dowody, że potrafi samodzielnie projektować backend.

Najbardziej obiecującym MVP nie jest więc ogromna interaktywna platforma. Jest nim bardzo mała pętla:

```text
Personal Goal
      ↓
Competency
      ↓
Realistic Challenge
      ↓
Decision / Code / Artifact
      ↓
Mentor Review
      ↓
Reflection / Retry
      ↓
Evidence of Mastery
      ↓
Next Challenge
```

Jeżeli ta pętla okaże się na tyle dobra, że Twoja pierwsza mentee po kilku miesiącach faktycznie **podejmuje lepsze decyzje, potrafi lepiej je uzasadniać i potrzebuje od Ciebie mniej prowadzenia**, masz fundament produktu.

Reszta — AI, automatyzacja GitHuba, rekomendacje, gamifikacja, multi-tenant, marketplace — jest już tylko skalowaniem tego fundamentu.