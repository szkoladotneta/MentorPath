using MentorPath.Application.Common;
using MentorPath.Data.Enums;

namespace MentorPath.Application.Catalog.Contracts;

public interface ITopicService
{
    Task<Result<IReadOnlyList<TopicListItem>>> ListAsync(Guid actorUserId, bool includeArchived, CancellationToken ct);
    Task<Result<TopicDetail>> GetAsync(Guid actorUserId, Guid topicId, CancellationToken ct);
    Task<Result<Guid>> CreateAsync(Guid actorUserId, UpsertTopicRequest request, CancellationToken ct);
    Task<Result> UpdateAsync(Guid actorUserId, Guid topicId, UpsertTopicRequest request, CancellationToken ct);
    Task<Result> SetArchivedAsync(Guid actorUserId, Guid topicId, bool isArchived, CancellationToken ct);
}

public sealed record UpsertTopicRequest(string Title, string Slug, string Category, Difficulty Difficulty,
    string DescriptionMarkdown, string LearningObjectivesMarkdown);
public sealed record TopicListItem(Guid TopicId, string Title, string Slug, string Category, Difficulty Difficulty, bool IsArchived, int ExerciseCount);
public sealed record TopicDetail(Guid TopicId, string Title, string Slug, string Category, Difficulty Difficulty,
    string DescriptionMarkdown, string LearningObjectivesMarkdown, bool IsArchived, IReadOnlyList<ExerciseListItem> Exercises);

public interface IExerciseAuthoringService
{
    Task<Result<Guid>> CreateDraftAsync(Guid actorUserId, CreateExerciseDraftRequest request, CancellationToken ct);
    Task<Result> UpdateDraftAsync(Guid actorUserId, Guid exerciseId, ExerciseContent content, CancellationToken ct);
    Task<Result> PublishAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result<Guid>> CloneAsDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result> ArchiveAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result> DeleteDraftAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result<MentorExerciseDto>> GetMentorExerciseAsync(Guid actorUserId, Guid exerciseId, CancellationToken ct);
    Task<Result<IReadOnlyList<ExerciseListItem>>> ListAsync(Guid actorUserId, ExerciseListFilter filter, CancellationToken ct);
    Task<Result<MenteeExerciseDto>> GetMenteeExerciseAsync(Guid actorUserId, Guid assignmentId, CancellationToken ct);
}

public sealed record CreateExerciseDraftRequest(Guid TopicId, ExerciseType Type, ExerciseContent Content);
public sealed record ExerciseContent(
    string Title, string ScenarioMarkdown, string? InstructionsMarkdown, string? ExpectedOutcomeMarkdown,
    string? RubricMarkdown, string? DiscussionQuestionsMarkdown, string? ExplanationMarkdown,
    string? MentorGuideMarkdown, int? EstimatedMinutes, IReadOnlyList<QuizOptionInput> QuizOptions);
public sealed record QuizOptionInput(string Text, bool IsCorrect, string? ExplanationMarkdown);
public sealed record ExerciseListFilter(Guid? TopicId, ExerciseStatus? Status, ExerciseType? Type);
public sealed record ExerciseListItem(Guid ExerciseId, Guid TopicId, string Title, ExerciseType Type, ExerciseStatus Status, ExerciseSource Source, DateTime CreatedAtUtc);
public sealed record MentorExerciseDto(Guid ExerciseId, Guid TopicId, string TopicTitle, ExerciseType Type, ExerciseStatus Status,
    ExerciseSource Source, ExerciseContent Content, DateTime? PublishedAtUtc, DateTime CreatedAtUtc);

// Mentee-safe projection: by design has no IsCorrect, MentorGuideMarkdown or ExplanationMarkdown.
public sealed record MenteeExerciseDto(Guid ExerciseId, ExerciseType Type, string Title, string ScenarioMarkdown,
    string? InstructionsMarkdown, string? ExpectedOutcomeMarkdown, string? RubricMarkdown,
    string? DiscussionQuestionsMarkdown, int? EstimatedMinutes, IReadOnlyList<MenteeQuizOptionDto> QuizOptions);
public sealed record MenteeQuizOptionDto(Guid QuizOptionId, string Text, int Position);
