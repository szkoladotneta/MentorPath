using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Application.Workspaces.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;

namespace MentorPath.IntegrationTests.Infrastructure;

public sealed record MentorRef(Guid UserId, Guid WorkspaceId, string Email);
public sealed record MenteeRef(Guid UserId, Guid MentorshipId, string Email);
public sealed record AssignmentRef(Guid RoadmapId, Guid RoadmapItemId, Guid AssignmentId, Guid ExerciseId, Guid TopicId);

/// <summary>Builds realistic data through the real application services. Every call creates fresh, uniquely named data.</summary>
public sealed partial class TestDataBuilder(TestApp app)
{
    public const string Password = "Test-password-123";

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 13)];

    public async Task<MentorRef> MentorAsync()
    {
        var email = $"{Unique("mentor")}@test.local";
        var result = (await app.Get<IMentorProvisioningService>().ProvisionMentorAsync(new(email, "Mentor " + email[..12], "WS " + email[..12]), default)).ShouldSucceed();
        var users = app.Get<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(result.UserId.ToString());
        (await users.AddPasswordAsync(user!, Password)).Succeeded.ShouldBeTrue();
        return new MentorRef(result.UserId, result.WorkspaceId, email);
    }

    public async Task<MenteeRef> MenteeAsync(MentorRef mentor, string? existingEmail = null, Guid? existingUserId = null)
    {
        var invitations = app.Get<IInvitationService>();
        var email = existingEmail ?? $"{Unique("mentee")}@test.local";
        var invite = (await invitations.CreateAsync(mentor.UserId, new(email), default)).ShouldSucceed();
        var token = TokenOf(invite.AcceptUrl);
        var accepted = existingUserId is null
            ? (await app.Get<IInvitationService>().AcceptWithNewAccountAsync(new(token, "Mentee " + email[..10], Password), default)).ShouldSucceed()
            : (await app.Get<IInvitationService>().AcceptAsExistingUserAsync(existingUserId.Value, token, default)).ShouldSucceed();
        return new MenteeRef(accepted.UserId, accepted.MentorshipId, email);
    }

    public static string TokenOf(string acceptUrl) => acceptUrl[(acceptUrl.LastIndexOf('/') + 1)..];

    public async Task<Guid> TopicAsync(MentorRef mentor, string? title = null)
    {
        var slugBase = Unique("topic");
        return (await app.Get<ITopicService>().CreateAsync(mentor.UserId, new(title ?? slugBase, slugBase, "Architektura",
            Difficulty.Intermediate, "Opis tematu", "- Cel"), default)).ShouldSucceed();
    }

    public static ExerciseContent ValidContent(ExerciseType type) => type switch
    {
        ExerciseType.QuizSingleChoice => new("Quiz", "Scenariusz quizu", null, null, null, null, "Wyjaśnienie", "Guide quizu", 10,
            [new("Poprawna", true, "Tak"), new("Błędna", false, "Nie"), new("Też błędna", false, "Nie")]),
        ExerciseType.PracticalTask => new("Zadanie", "Opis zadania", "Wymagania", "Artefakty", "Kryteria", "- Pytanie?", null, "Guide PT", 120, []),
        _ => new("Decyzja", "Scenariusz decyzji", "Ograniczenia", "Rezultaty", "Kryteria", null, null, "Guide AD", 60, []),
    };

    public async Task<Guid> PublishedExerciseAsync(MentorRef mentor, Guid topicId, ExerciseType type, IReadOnlyList<Guid>? criterionIds = null)
    {
        var exercises = app.Get<IExerciseAuthoringService>();
        var id = (await exercises.CreateDraftAsync(mentor.UserId, new(topicId, type, ValidContent(type) with { CriterionIds = criterionIds }), default)).ShouldSucceed();
        (await exercises.PublishAsync(mentor.UserId, id, default)).ShouldSucceed();
        return id;
    }

    /// <summary>Creates a published roadmap for the mentee with one topic and one required assignment of the given type.</summary>
    public async Task<AssignmentRef> PublishedAssignmentAsync(MentorRef mentor, MenteeRef mentee, ExerciseType type, bool publish = true, IReadOnlyList<Guid>? criterionIds = null)
    {
        var roadmaps = app.Get<IRoadmapService>();
        var topicId = await TopicAsync(mentor);
        var exerciseId = await PublishedExerciseAsync(mentor, topicId, type, criterionIds);
        var existing = (await roadmaps.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        var roadmapId = existing?.RoadmapId ?? (await roadmaps.CreateAsync(mentor.UserId, new(mentee.MentorshipId, "Roadmapa", "Cel"), default)).ShouldSucceed();
        var itemId = (await roadmaps.AddTopicAsync(mentor.UserId, new(roadmapId, topicId, null), default)).ShouldSucceed();
        var assignmentId = (await roadmaps.AssignExerciseAsync(mentor.UserId, new(itemId, exerciseId, true, null), default)).ShouldSucceed();
        if (publish && existing?.Status != RoadmapStatus.Published)
            (await roadmaps.PublishAsync(mentor.UserId, roadmapId, default)).ShouldSucceed();
        return new AssignmentRef(roadmapId, itemId, assignmentId, exerciseId, topicId);
    }

    /// <summary>Scores every active criterion of the mentor's workspace (the default selection of a new exercise).</summary>
    public async Task<IReadOnlyList<CriterionScoreInput>> ScoresAsync(MentorRef mentor, int score = 4)
    {
        var criteria = (await app.Get<IRubricService>().ListCriteriaAsync(mentor.UserId, false, default)).ShouldSucceed();
        return criteria.Select(c => new CriterionScoreInput(c.CriterionId, score, "")).ToList();
    }

    public async Task<SubmissionResultDto> DecisionSubmissionAsync(MenteeRef mentee, Guid assignmentId) =>
        (await app.Get<ISubmissionService>().SubmitDecisionAsync(mentee.UserId, new(assignmentId, Guid.NewGuid(),
            new("Modularny monolit", "Mały zespół", "Mikroserwisy", "Prostota vs skalowanie", "Rozrost modułów", "Gdy zespół > 3"), 3), default)).ShouldSucceed();
}
