using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Web.Components.Shared;

public static class Labels
{
    public static string Of(ExerciseType v) => v switch
    {
        ExerciseType.QuizSingleChoice => "Quiz",
        ExerciseType.ArchitecturalDecision => "Decyzja architektoniczna",
        ExerciseType.PracticalTask => "Zadanie praktyczne",
        _ => v.ToString(),
    };

    public static string Of(ExerciseStatus v) => v switch
    {
        ExerciseStatus.Draft => "Szkic",
        ExerciseStatus.Published => "Opublikowane",
        ExerciseStatus.Archived => "Zarchiwizowane",
        _ => v.ToString(),
    };

    public static string Of(SubmissionStatus v) => v switch
    {
        SubmissionStatus.Correct => "Poprawna",
        SubmissionStatus.Incorrect => "Niepoprawna",
        SubmissionStatus.PendingReview => "Czeka na ocenę",
        SubmissionStatus.Accepted => "Zaakceptowana",
        SubmissionStatus.NeedsRevision => "Do poprawy",
        _ => v.ToString(),
    };

    public static string Of(MentorshipStatus v) => v switch
    {
        MentorshipStatus.Active => "Aktywny",
        MentorshipStatus.Paused => "Wstrzymany",
        MentorshipStatus.Closed => "Zamknięty",
        _ => v.ToString(),
    };

    public static string Of(RoadmapStatus v) => v == RoadmapStatus.Published ? "Opublikowana" : "Szkic";

    public static string Of(CompetencyStatus v) => v switch
    {
        CompetencyStatus.NotAssessed => "Nieoceniona",
        CompetencyStatus.Learning => "W trakcie nauki",
        CompetencyStatus.Practicing => "Ćwiczona",
        CompetencyStatus.Demonstrated => "Wykazana",
        CompetencyStatus.NeedsRevisit => "Do powtórki",
        _ => v.ToString(),
    };

    public static string Of(Difficulty v) => v switch
    {
        Difficulty.Beginner => "Podstawowy",
        Difficulty.Intermediate => "Średni",
        Difficulty.Advanced => "Zaawansowany",
        _ => v.ToString(),
    };

    public static string Of(ExperienceLevel v) => v switch
    {
        ExperienceLevel.Junior => "Junior",
        ExperienceLevel.Mid => "Mid",
        ExperienceLevel.Senior => "Senior",
        ExperienceLevel.Lead => "Lead / Architekt",
        _ => v.ToString(),
    };

    public static string Of(ReviewVerdict v) => v == ReviewVerdict.Accepted ? "Zaakceptowane" : "Do poprawy";

    public static string Of(InvitationState v) => v switch
    {
        InvitationState.Pending => "Oczekuje",
        InvitationState.Accepted => "Przyjęte",
        _ => "Wygasło",
    };

    public static string Of(AssignmentProgressStatus v) => v switch
    {
        AssignmentProgressStatus.NotStarted => "Nierozpoczęte",
        AssignmentProgressStatus.InProgress => "W trakcie",
        AssignmentProgressStatus.PendingReview => "Czeka na ocenę",
        AssignmentProgressStatus.NeedsRevision => "Do poprawy",
        _ => "Zaliczone",
    };

    public static string Of(TopicProgressStatus v) => v switch
    {
        TopicProgressStatus.NotStarted => "Nierozpoczęty",
        TopicProgressStatus.InProgress => "W trakcie",
        _ => "Ukończony",
    };

    public static string Of(ActivityKind v) => v switch
    {
        ActivityKind.Submitted => "przesłał(a) rozwiązanie",
        ActivityKind.FeedbackGiven => "otrzymał(a) feedback",
        _ => "dodał(a) refleksję",
    };

    public static string Of(MapNodeState v) => v switch
    {
        MapNodeState.NotStarted => "Nierozpoczęty",
        MapNodeState.InProgress => "W trakcie",
        MapNodeState.PendingReview => "Czeka na ocenę",
        MapNodeState.NeedsRevision => "Do poprawy",
        MapNodeState.Completed => "Ukończony",
        _ => "Wycofany",
    };

    public static string Icon(MapNodeState v) => v switch
    {
        MapNodeState.NotStarted => "○",
        MapNodeState.InProgress => "◐",
        MapNodeState.PendingReview => "…",
        MapNodeState.NeedsRevision => "!",
        MapNodeState.Completed => "✓",
        _ => "–",
    };

    public static string Tone(object v) => v switch
    {
        SubmissionStatus.Correct or SubmissionStatus.Accepted or AssignmentProgressStatus.Completed or TopicProgressStatus.Completed
            or ExerciseStatus.Published or RoadmapStatus.Published or MentorshipStatus.Active or InvitationState.Accepted
            or CompetencyStatus.Demonstrated => "success",
        SubmissionStatus.PendingReview or AssignmentProgressStatus.PendingReview or InvitationState.Pending => "info",
        SubmissionStatus.NeedsRevision or SubmissionStatus.Incorrect or AssignmentProgressStatus.NeedsRevision
            or MentorshipStatus.Paused or AssignmentProgressStatus.InProgress or TopicProgressStatus.InProgress
            or CompetencyStatus.Learning or CompetencyStatus.Practicing => "warning",
        ExerciseStatus.Archived or MentorshipStatus.Closed or InvitationState.Expired or CompetencyStatus.NeedsRevisit => "secondary",
        _ => "light",
    };

    public static string Date(DateTime? utc) => utc is null ? "—" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string Day(DateTime? utc) => utc is null ? "—" : utc.Value.ToLocalTime().ToString("dd.MM.yyyy");

    public static string Size(long bytes) => bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";
}
