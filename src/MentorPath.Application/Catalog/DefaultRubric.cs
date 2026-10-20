namespace MentorPath.Application.Catalog;

/// <summary>Default decision-process criteria (D-38). Mirrors the seed in migration AddDecisionRubric.</summary>
internal static class DefaultRubric
{
    public sealed record Criterion(string Name, string[] Levels);

    public static readonly IReadOnlyList<Criterion> Criteria =
    [
        new("Rozpoznanie ograniczeń",
        [
            "Nie wskazuje ograniczeń ani wymagań problemu.",
            "Wskazuje pojedyncze ograniczenia, pomija kluczowe (skala, spójność, koszt, zespół).",
            "Rozpoznaje główne ograniczenia, ale bez priorytetów i bez weryfikacji założeń.",
            "Rozpoznaje i priorytetyzuje ograniczenia oraz jawnie nazywa założenia.",
            "Pełny obraz ograniczeń z priorytetami, założeniami i wskazaniem, co trzeba doprecyzować.",
        ]),
        new("Alternatywy",
        [
            "Przedstawia tylko jedno rozwiązanie.",
            "Wspomina o alternatywie, ale bez sensownego opisu.",
            "Opisuje co najmniej dwie realne opcje.",
            "Opisuje realne opcje i uzasadnia, dlaczego odrzuca pozostałe.",
            "Systematycznie porównuje opcje, w tym „nic nie zmieniaj” i rozwiązania prostsze.",
        ]),
        new("Trade-offs",
        [
            "Nie wskazuje kosztów ani wad wybranego rozwiązania.",
            "Wymienia ogólne wady bez powiązania z kontekstem.",
            "Wskazuje główne kompromisy wybranego rozwiązania.",
            "Wskazuje kompromisy w kontekście ograniczeń i uzasadnia ich akceptację.",
            "Waży kompromisy i pokazuje, co świadomie zyskujemy, a co tracimy.",
        ]),
        new("Ewolucja rozwiązania",
        [
            "Brak myślenia o rozwoju rozwiązania w czasie.",
            "Wspomina o przyszłych zmianach bez konkretów.",
            "Opisuje, jak rozwiązanie może się zmieniać przy wzroście wymagań.",
            "Wskazuje sygnały zmiany i bezpieczne kroki migracji.",
            "Planuje ewolucję: wyraźne triggery zmiany, koszt migracji i odwracalność decyzji.",
        ]),
        new("Operacyjność",
        [
            "Nie uwzględnia wdrożenia ani utrzymania.",
            "Wspomina o monitoringu lub wdrożeniu bez szczegółów.",
            "Uwzględnia wdrożenie, monitoring i obsługę błędów na podstawowym poziomie.",
            "Opisuje obserwowalność, scenariusze awarii i sposób odzyskiwania.",
            "Kompleksowo: failure modes, SLO, koszty utrzymania, rollback i obciążenie zespołu.",
        ]),
        new("Komunikacja",
        [
            "Odpowiedź chaotyczna, trudna do zrozumienia.",
            "Zrozumiała w części, brak struktury.",
            "Czytelna i uporządkowana, ale bez dostosowania do odbiorcy.",
            "Jasna, ustrukturyzowana, z uzasadnieniem decyzji dla odbiorcy.",
            "Zwięzła i przekonująca; oddziela fakty, założenia i rekomendację oraz wskazuje ryzyka.",
        ]),
    ];
}
