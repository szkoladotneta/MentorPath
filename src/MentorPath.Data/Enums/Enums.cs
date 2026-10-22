namespace MentorPath.Data.Enums;

public enum ExerciseType { QuizSingleChoice, ArchitecturalDecision, PracticalTask }

public enum ExerciseStatus { Draft, Published, Archived }

public enum ExerciseSource { Manual, AI }

public enum TopicResourceKind { Article, Video }

public enum RoadmapStatus { Draft, Published }

public enum SubmissionStatus { Correct, Incorrect, PendingReview, Accepted, NeedsRevision }

public enum MentorshipStatus { Active, Paused, Closed }

public enum AiRequestStatus { Pending, Completed, Failed }

public enum ReviewVerdict { Accepted, NeedsRevision }

public enum Difficulty { Beginner, Intermediate, Advanced }

public enum ExperienceLevel { Junior, Mid, Senior, Lead }

public static class AppRoles
{
    public const string Mentor = "Mentor";
    public const string Mentee = "Mentee";
    public const string PlatformOperator = "PlatformOperator";

    public static readonly string[] All = [Mentor, Mentee, PlatformOperator];
}
