using MentorPath.Application.AiAuthoring.Contracts;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.AiAuthoring;

public sealed class AiDraftTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private static GenerateExerciseDraftRequest Request(Guid topic, ExerciseType type) =>
        new(topic, type, Difficulty.Intermediate, "Analiza kompromisów", "Zespół 4 osób", 45);

    [Theory]
    [InlineData(ExerciseType.QuizSingleChoice)]
    [InlineData(ExerciseType.ArchitecturalDecision)]
    [InlineData(ExerciseType.PracticalTask)]
    public async Task Generated_content_is_always_a_validated_draft(ExerciseType type)
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var result = (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId, Request(topic, type), default)).ShouldSucceed();

        var exercise = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, result.ExerciseId, default)).ShouldSucceed();
        exercise.Status.ShouldBe(ExerciseStatus.Draft);
        exercise.Source.ShouldBe(ExerciseSource.AI);
        exercise.Type.ShouldBe(type);
        exercise.Content.MentorGuideMarkdown.ShouldNotBeNullOrWhiteSpace();

        var log = await App.Db(ctx => ctx.AiGenerationRequests.SingleAsync(r => r.Id == result.AiRequestId));
        log.Status.ShouldBe(AiRequestStatus.Completed);
        log.ExerciseId.ShouldBe(result.ExerciseId);
        log.InputTokens.ShouldNotBeNull();

        // Publication still requires an explicit mentor action.
        (await App.Get<IExerciseAuthoringService>().PublishAsync(mentor.UserId, result.ExerciseId, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Rejecting_ai_draft_deletes_it_but_keeps_generation_log()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var result = (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId, Request(topic, ExerciseType.QuizSingleChoice), default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().DeleteDraftAsync(mentor.UserId, result.ExerciseId, default)).ShouldSucceed();
        var log = await App.Db(ctx => ctx.AiGenerationRequests.SingleAsync(r => r.Id == result.AiRequestId));
        log.ExerciseId.ShouldBeNull();
    }

    [Fact]
    public async Task Other_mentor_cannot_generate_for_foreign_topic()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var topic = await Data.TopicAsync(a);
        (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(b.UserId, Request(topic, ExerciseType.QuizSingleChoice), default)).ShouldBeNotFound();
    }
}

public sealed class AiInvalidOutputTests(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?> { ["Ai:FakeMode"] = "InvalidJson" };

    [Fact]
    public async Task Invalid_json_is_rejected_and_no_exercise_is_created()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId,
            new(topic, ExerciseType.QuizSingleChoice, Difficulty.Beginner, "Cele", null, null), default))
            .ShouldFail(ErrorKind.Validation, "ai.invalid_output");
        (await App.Db(ctx => ctx.Exercises.CountAsync(e => e.WorkspaceId == mentor.WorkspaceId))).ShouldBe(0);
        var log = await App.Db(ctx => ctx.AiGenerationRequests.SingleAsync(r => r.WorkspaceId == mentor.WorkspaceId));
        log.Status.ShouldBe(AiRequestStatus.Failed);
        log.ErrorCode.ShouldBe("invalid_json");
    }
}

public sealed class AiProviderOutageTests(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?> { ["Ai:FakeMode"] = "Timeout", ["Ai:TimeoutSeconds"] = "1" };

    [Fact]
    public async Task Provider_timeout_returns_readable_error_and_manual_authoring_still_works()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var error = (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId,
            new(topic, ExerciseType.ArchitecturalDecision, Difficulty.Beginner, "Cele", null, null), default))
            .ShouldFail(ErrorKind.Unavailable, "ai.provider_unavailable");
        error.Message.ShouldContain("ręcznie");

        var manual = await Data.PublishedExerciseAsync(mentor, topic, ExerciseType.ArchitecturalDecision);
        manual.ShouldNotBe(Guid.Empty);
        (await App.Db(ctx => ctx.AiGenerationRequests.SingleAsync(r => r.WorkspaceId == mentor.WorkspaceId))).ErrorCode.ShouldBe("timeout");
    }
}

public sealed class AiQuotaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?> { ["Ai:MonthlyGenerationLimitPerWorkspace"] = "2" };

    [Fact]
    public async Task Generation_limit_per_workspace_is_enforced()
    {
        var mentor = await Data.MentorAsync();
        var other = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var request = new GenerateExerciseDraftRequest(topic, ExerciseType.QuizSingleChoice, Difficulty.Beginner, "Cele", null, null);
        (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId, request, default)).ShouldSucceed();
        (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId, request, default)).ShouldSucceed();
        (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentor.UserId, request, default)).ShouldFail(ErrorKind.QuotaExceeded);

        (await App.Get<IAiExerciseDraftService>().GetQuotaAsync(mentor.UserId, default)).ShouldSucceed().Used.ShouldBe(2);
        (await App.Get<IAiExerciseDraftService>().GetQuotaAsync(other.UserId, default)).ShouldSucceed().Used.ShouldBe(0);
    }
}
