using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;

namespace MentorPath.Web.Cli;

/// <summary>Demo data for two mentors and two mentees, built through the real application services (B16).</summary>
internal static class DemoSeed
{
    public const string MentorAEmail = "mentor.a@mentorpath.local";
    public const string MentorBEmail = "mentor.b@mentorpath.local";
    public const string MenteeOlaEmail = "ola@mentorpath.local";
    public const string MenteePiotrEmail = "piotr@mentorpath.local";

    public static async Task<int> RunAsync(IServiceProvider sp, string password, TextWriter log)
    {
        var ct = CancellationToken.None;
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(MentorAEmail) is not null)
        {
            log.WriteLine("Demo data already present — nothing to do.");
            return 0;
        }

        var provisioning = sp.GetRequiredService<IMentorProvisioningService>();
        var invitations = sp.GetRequiredService<IInvitationService>();
        var mentorships = sp.GetRequiredService<IMentorshipService>();
        var topics = sp.GetRequiredService<ITopicService>();
        var exercises = sp.GetRequiredService<IExerciseAuthoringService>();
        var roadmaps = sp.GetRequiredService<IRoadmapService>();
        var submissions = sp.GetRequiredService<ISubmissionService>();

        var mentorA = Ok(await provisioning.ProvisionMentorAsync(new("mentor.a@mentorpath.local", "Anna Architekt", "Mentoring Anny"), ct));
        var mentorB = Ok(await provisioning.ProvisionMentorAsync(new("mentor.b@mentorpath.local", "Bartek Backend", "Mentoring Bartka"), ct));
        foreach (var id in new[] { mentorA.UserId, mentorB.UserId })
        {
            var u = (await users.FindByIdAsync(id.ToString()))!;
            Ok(await users.AddPasswordAsync(u, password));
        }

        // Mentees join through real invitations (Ola: both workspaces, Piotr: workspace A).
        var inviteOla = Ok(await invitations.CreateAsync(mentorA.UserId, new(MenteeOlaEmail), ct));
        var ola = Ok(await invitations.AcceptWithNewAccountAsync(new(TokenOf(inviteOla.AcceptUrl), "Ola Developer", password), ct));
        var invitePiotr = Ok(await invitations.CreateAsync(mentorA.UserId, new(MenteePiotrEmail), ct));
        var piotr = Ok(await invitations.AcceptWithNewAccountAsync(new(TokenOf(invitePiotr.AcceptUrl), "Piotr Programista", password), ct));
        var inviteOlaB = Ok(await invitations.CreateAsync(mentorB.UserId, new(MenteeOlaEmail), ct));
        var olaB = Ok(await invitations.AcceptAsExistingUserAsync(ola.UserId, TokenOf(inviteOlaB.AcceptUrl), ct));

        Ok(await mentorships.SaveOnboardingAsync(ola.UserId, ola.MentorshipId, new(
            "Chcę samodzielnie projektować moduły backendu i świadomie wybierać kompromisy.", ExperienceLevel.Mid,
            "Granice modułów, spójność danych, komunikacja asynchroniczna.", 6), ct));
        Ok(await mentorships.UpdatePrivateNotesAsync(mentorA.UserId, ola.MentorshipId,
            "Mocna w implementacji, potrzebuje pracy nad uzasadnianiem decyzji.", ct));

        // Workspace A catalog.
        var tModular = Ok(await topics.CreateAsync(mentorA.UserId, new("Modularny monolit", "modularny-monolit", "Architektura",
            Difficulty.Intermediate, "Projektowanie granic modułów w jednym procesie.", "- Wyznaczanie granic modułów\n- Kontrakty między modułami"), ct));
        var tConsistency = Ok(await topics.CreateAsync(mentorA.UserId, new("Spójność danych i transakcje", "spojnosc-danych", "Dane",
            Difficulty.Intermediate, "Transakcje, outbox, sagi i ich koszty.", "- Dobór modelu spójności\n- Świadomość kosztów"), ct));
        var tIdempotency = Ok(await topics.CreateAsync(mentorA.UserId, new("Idempotencja API", "idempotencja-api", "Niezawodność",
            Difficulty.Advanced, "Bezpieczne ponawianie żądań i deduplikacja.", "- Klucze idempotencji\n- Ochrona przed wyścigami"), ct));

        var exDecision = await PublishedAsync(exercises, mentorA.UserId, new(tModular, ExerciseType.ArchitecturalDecision, new(
            "Podział systemu rezerwacji na moduły",
            "Startup rozwija platformę rezerwacji. Zespół 4 osób, ruch rośnie 20% miesięcznie. CTO proponuje mikroserwisy.",
            "- Ograniczony budżet infrastruktury\n- Wdrożenia kilka razy dziennie\n- Jeden zespół", "Decyzja z uzasadnieniem, alternatywami, kompromisami, ryzykami i warunkami ponownej oceny.",
            "- Analiza alternatyw\n- Koszty operacyjne\n- Konkretne warunki rewizji", null, null,
            "Dobra odpowiedź zwykle wskazuje modularny monolit z kryteriami wydzielenia usług.", 60, [])), ct);
        var exQuiz = await PublishedAsync(exercises, mentorA.UserId, new(tConsistency, ExerciseType.QuizSingleChoice, new(
            "Spójność zamówienia i rezerwacji",
            "Moduł zamówień i moduł magazynu działają w jednym procesie i jednej bazie PostgreSQL. Jak zapewnić spójność przy tworzeniu zamówienia?",
            "Wybierz jedną odpowiedź i uzasadnij.", null, null, null,
            "W jednym procesie i jednej bazie najprostsza i spójna jest jedna transakcja; saga i 2PC są uzasadnione dopiero po rozdzieleniu.",
            "Sprawdź, czy mentee rozumie koszt rozproszonych transakcji.", 10,
            [new("Jedna transakcja bazodanowa", true, "Najprostsze i spójne rozwiązanie."),
             new("Saga z kompensacjami przez event bus", false, "Nadmiarowa złożoność bez rozdzielenia procesów."),
             new("Two-phase commit", false, "Kosztowne i zbędne w jednej bazie.")])), ct);
        var exPractical = await PublishedAsync(exercises, mentorA.UserId, new(tIdempotency, ExerciseType.PracticalTask, new(
            "Idempotentny endpoint płatności",
            "Zaimplementuj endpoint przyjmujący płatności, który bezpiecznie obsługuje ponowienia żądań.",
            "- ASP.NET Core Minimal API\n- PostgreSQL + EF Core\n- Klucz idempotencji w nagłówku",
            "Repozytorium z kodem, testami integracyjnymi i krótkim ADR.",
            "- Idempotencja przy współbieżności\n- Jakość testów\n- Uzasadnienie w ADR",
            "- Jak długo przechowujesz klucze idempotencji?\n- Co zwracasz przy konflikcie treści żądania?", null,
            "Sprawdź, czy unikalny indeks chroni przed wyścigiem.", 240, [])), ct);

        // Roadmap for Ola in workspace A with three topics.
        var roadmapA = Ok(await roadmaps.CreateAsync(mentorA.UserId, new(ola.MentorshipId, "Ścieżka: decyzje architektoniczne",
            "Samodzielne podejmowanie decyzji o granicach modułów i spójności danych."), ct));
        var iModular = Ok(await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapA, tModular, "Zacznij od tego — to fundament kolejnych tematów."), ct));
        var iConsistency = Ok(await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapA, tConsistency, null), ct));
        var iIdempotency = Ok(await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapA, tIdempotency, null), ct));
        Ok(await roadmaps.AssignExerciseAsync(mentorA.UserId, new(iModular, exDecision, true, DateTime.UtcNow.AddDays(14)), ct));
        var aQuiz = Ok(await roadmaps.AssignExerciseAsync(mentorA.UserId, new(iConsistency, exQuiz, true, null), ct));
        Ok(await roadmaps.AssignExerciseAsync(mentorA.UserId, new(iIdempotency, exPractical, true, DateTime.UtcNow.AddDays(30)), ct));
        Ok(await roadmaps.PublishAsync(mentorA.UserId, roadmapA, ct));

        var quizOptions = Ok(await exercises.GetMenteeExerciseAsync(ola.UserId, aQuiz, ct)).QuizOptions;
        Ok(await submissions.SubmitQuizAsync(ola.UserId, new(aQuiz, Guid.NewGuid(), quizOptions[0].QuizOptionId,
            "Jedna baza i jeden proces — transakcja wystarczy.", 4), ct));

        // Workspace B: separate catalog and roadmap for Ola.
        var tB = Ok(await topics.CreateAsync(mentorB.UserId, new("Wzorce integracji", "wzorce-integracji", "Integracje",
            Difficulty.Beginner, "Synchroniczna i asynchroniczna komunikacja.", "- Dobór stylu integracji"), ct));
        var exB = await PublishedAsync(exercises, mentorB.UserId, new(tB, ExerciseType.ArchitecturalDecision, new(
            "REST czy kolejka?", "System zamówień musi powiadamiać system księgowy o każdej fakturze.",
            "- Księgowość bywa niedostępna\n- Faktury nie mogą zginąć", "Decyzja ze wszystkimi sekcjami.",
            "- Niezawodność\n- Prostota", null, null, "Szukaj outboxa lub kolejki z retry.", 45, [])), ct);
        var roadmapB = Ok(await roadmaps.CreateAsync(mentorB.UserId, new(olaB.MentorshipId, "Integracje systemów", "Świadomy dobór stylu integracji."), ct));
        var iB = Ok(await roadmaps.AddTopicAsync(mentorB.UserId, new(roadmapB, tB, null), ct));
        Ok(await roadmaps.AssignExerciseAsync(mentorB.UserId, new(iB, exB, true, null), ct));
        Ok(await roadmaps.PublishAsync(mentorB.UserId, roadmapB, ct));

        log.WriteLine("Demo data created. Accounts (password: as provided):");
        log.WriteLine($"  Mentor A: {MentorAEmail}");
        log.WriteLine($"  Mentor B: {MentorBEmail}");
        log.WriteLine($"  Mentee (A + B): {MenteeOlaEmail}");
        log.WriteLine($"  Mentee (A): {MenteePiotrEmail} (no roadmap yet)");
        _ = piotr;
        return 0;
    }

    private static async Task<Guid> PublishedAsync(IExerciseAuthoringService exercises, Guid mentor, CreateExerciseDraftRequest request, CancellationToken ct)
    {
        var id = Ok(await exercises.CreateDraftAsync(mentor, request, ct));
        Ok(await exercises.PublishAsync(mentor, id, ct));
        return id;
    }

    private static string TokenOf(string acceptUrl) => acceptUrl[(acceptUrl.LastIndexOf('/') + 1)..];

    private static T Ok<T>(Result<T> result) =>
        result.IsSuccess ? result.Value! : throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message} {Fields(result.Error)}");

    private static void Ok(Result result)
    {
        if (!result.IsSuccess) throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message} {Fields(result.Error)}");
    }

    private static void Ok(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private static string Fields(AppError e) => e.FieldErrors is null ? "" : string.Join("; ", e.FieldErrors.Select(f => $"{f.Key}: {string.Join(" ", f.Value)}"));
}
