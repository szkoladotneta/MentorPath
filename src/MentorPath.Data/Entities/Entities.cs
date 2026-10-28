using MentorPath.Data.Enums;

namespace MentorPath.Data.Entities;

public interface IWorkspaceScoped
{
    Guid WorkspaceId { get; }
}

public sealed class Workspace
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    public AppUser? Owner { get; set; }
}

public sealed class Mentorship : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid MenteeUserId { get; set; }
    public string? Goal { get; set; }
    public ExperienceLevel? CurrentLevel { get; set; }
    public int? WeeklyHours { get; set; }
    public string? Challenges { get; set; }
    public string? MentorPrivateNotes { get; set; }
    public MentorshipStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
    public AppUser? Mentee { get; set; }
    public Roadmap? Roadmap { get; set; }
}

public sealed class Invitation : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string NormalizedEmail { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
}

public sealed class Topic : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ParentTopicId { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public Difficulty Difficulty { get; set; }
    public string DescriptionMarkdown { get; set; } = "";
    public string? WhyItMattersMarkdown { get; set; }
    public string LearningObjectivesMarkdown { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class CompetencyPrerequisite : IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }
    public Guid TopicId { get; set; }
    public Guid PrerequisiteTopicId { get; set; }
}

public sealed class TopicResource : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TopicId { get; set; }
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public TopicResourceKind Kind { get; set; }
    public string? Description { get; set; }
    public int Position { get; set; }

    public Topic? Topic { get; set; }
}

public sealed class Exercise : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TopicId { get; set; }
    public ExerciseType Type { get; set; }
    public ExerciseStatus Status { get; set; }
    public ExerciseSource Source { get; set; }
    public string Title { get; set; } = "";
    public string ScenarioMarkdown { get; set; } = "";
    public string InstructionsMarkdown { get; set; } = "";
    public string? ExpectedOutcomeMarkdown { get; set; }
    public string RubricMarkdown { get; set; } = "";
    public string? DiscussionQuestionsMarkdown { get; set; }
    public string MentorGuideMarkdown { get; set; } = "";
    public string? ExplanationMarkdown { get; set; }
    public int? EstimatedMinutes { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Topic? Topic { get; set; }
    public List<QuizOption> QuizOptions { get; set; } = [];
}

public sealed class QuizOption
{
    public Guid Id { get; set; }
    public Guid ExerciseId { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string ExplanationMarkdown { get; set; } = "";
    public int Position { get; set; }
}

public sealed class Roadmap : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid MentorshipId { get; set; }
    public string Title { get; set; } = "";
    public string ObjectiveMarkdown { get; set; } = "";
    public RoadmapStatus Status { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Mentorship? Mentorship { get; set; }
    public List<RoadmapItem> Items { get; set; } = [];
}

public sealed class RoadmapItem : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RoadmapId { get; set; }
    public Guid TopicId { get; set; }
    public int Position { get; set; }
    public string? MentorNoteMarkdown { get; set; }
    public bool IsActive { get; set; }

    public Roadmap? Roadmap { get; set; }
    public Topic? Topic { get; set; }
    public List<Assignment> Assignments { get; set; } = [];
}

public sealed class Assignment : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RoadmapItemId { get; set; }
    public Guid ExerciseId { get; set; }
    public int Position { get; set; }
    public bool IsRequired { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DueAtUtc { get; set; }

    public RoadmapItem? RoadmapItem { get; set; }
    public Exercise? Exercise { get; set; }
    public List<Submission> Submissions { get; set; } = [];
}

public sealed class Submission : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid MenteeUserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int AttemptNumber { get; set; }
    public SubmissionStatus Status { get; set; }
    public Guid? SelectedQuizOptionId { get; set; }
    public string? DecisionAnswerJson { get; set; }
    public string? AnswerMarkdown { get; set; }
    public string? ExternalUrl { get; set; }
    public short ConfidenceBefore { get; set; }
    public DateTime SubmittedAtUtc { get; set; }

    public Assignment? Assignment { get; set; }
    public AppUser? Mentee { get; set; }
    public QuizOption? SelectedQuizOption { get; set; }
    public SubmissionAttachment? Attachment { get; set; }
    public MentorFeedback? Feedback { get; set; }
    public SubmissionReflection? Reflection { get; set; }
}

public sealed class SubmissionAttachment : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SubmissionId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Submission? Submission { get; set; }
}

public sealed class MentorFeedback : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid MentorUserId { get; set; }
    public ReviewVerdict Verdict { get; set; }
    public string StrengthsMarkdown { get; set; } = "";
    public string ImprovementsMarkdown { get; set; } = "";
    public string ChallengeQuestionMarkdown { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    public Submission? Submission { get; set; }
    public List<FeedbackCriterionScore> CriterionScores { get; set; } = [];
}

public sealed class ProductFeedback
{
    public Guid Id { get; set; }
    public Guid AuthorUserId { get; set; }
    public string MessageText { get; set; } = "";
    public string? PageContext { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public AppUser? Author { get; set; }
}

public sealed class RubricCriterion : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public bool IsActive { get; set; }
    public string Level1Description { get; set; } = "";
    public string Level2Description { get; set; } = "";
    public string Level3Description { get; set; } = "";
    public string Level4Description { get; set; } = "";
    public string Level5Description { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ExerciseCriterion : IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }
    public Guid ExerciseId { get; set; }
    public Guid CriterionId { get; set; }

    public Exercise? Exercise { get; set; }
    public RubricCriterion? Criterion { get; set; }
}

public sealed class FeedbackCriterionScore : IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }
    public Guid FeedbackId { get; set; }
    public Guid CriterionId { get; set; }
    public short Score { get; set; }
    public string CommentMarkdown { get; set; } = "";

    public MentorFeedback? Feedback { get; set; }
    public RubricCriterion? Criterion { get; set; }
}

public sealed class SubmissionReflection
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public short ConfidenceAfter { get; set; }
    public string TakeawayMarkdown { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }

    public Submission? Submission { get; set; }
}

public sealed class ActivityEvent : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid MenteeUserId { get; set; }
    public Guid MentorUserId { get; set; }
    public ActivityEventType EventType { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public TimeSpan? ReviewDuration { get; set; }
    public bool ExceedsTwoHours { get; set; }
}

public sealed class AiGenerationRequest : IWorkspaceScoped
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ExerciseId { get; set; }
    public ExerciseType ExerciseType { get; set; }
    public AiRequestStatus Status { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string PromptTemplateVersion { get; set; } = "";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
