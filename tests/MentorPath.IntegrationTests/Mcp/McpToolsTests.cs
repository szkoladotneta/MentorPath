using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Mcp;
using MentorPath.Web.Mcp.Resources;
using MentorPath.Web.Mcp.Tools;
using ModelContextProtocol;

namespace MentorPath.IntegrationTests.Mcp;

/// <summary>MCP tools are a thin layer over the application services: the actor they pass decides everything (D-64).</summary>
public sealed class McpToolsTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private sealed class FixedActor(Guid id) : IActorContext
    {
        public Guid ActorUserId => id;
    }

    [Fact]
    public async Task Read_tools_return_only_the_actors_own_workspace()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        await Data.MenteeAsync(b);
        var actor = new FixedActor(a.UserId);

        var mentees = await MenteeTools.ListMentees(App.Get<IMentorshipService>(), actor, default);
        mentees.Select(m => m.MentorshipId).ShouldBe([mentee.MentorshipId]);

        var profile = await MenteeTools.GetMenteeProfile(App.Get<IMentorshipService>(), actor, default, mentee.MentorshipId);
        profile.MentorshipId.ShouldBe(mentee.MentorshipId);

        var noRoadmap = await RoadmapTools.GetRoadmap(App.Get<IRoadmapService>(), actor, default, mentee.MentorshipId);
        noRoadmap.ShouldNotBeOfType<MentorRoadmapDto>(); // the hint object, not a roadmap

        var dashboard = await MenteeTools.GetMentorDashboard(App.Get<IProgressService>(), actor, default);
        dashboard.Mentees.Select(m => m.MentorshipId).ShouldBe([mentee.MentorshipId]);
    }

    [Fact]
    public async Task Every_tool_is_not_found_for_a_foreign_mentor_and_an_unknown_user()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.QuizSingleChoice);

        foreach (var foreign in new[] { new FixedActor(b.UserId), new FixedActor(Guid.NewGuid()) })
        {
            await ShouldFail("mentorship.not_found", () => MenteeTools.GetMenteeProfile(App.Get<IMentorshipService>(), foreign, default, mentee.MentorshipId));
            await ShouldFail("catalog.not_found", () => CatalogTools.GetTopic(App.Get<ITopicService>(), foreign, default, asg.TopicId));
            await ShouldFail("catalog.not_found", () => CatalogTools.GetExercise(App.Get<IExerciseAuthoringService>(), foreign, default, asg.ExerciseId));
            await ShouldFail("catalog.not_found", () => CatalogTools.CreateExerciseDraft(App.Get<IExerciseAuthoringService>(), foreign, default,
                asg.TopicId, ExerciseType.PracticalTask, "Zadanie", "Opis"));
            await ShouldFail("roadmap.not_found", () => RoadmapTools.GetRoadmap(App.Get<IRoadmapService>(), foreign, default, mentee.MentorshipId));
            await ShouldFail("progress.not_found", () => RoadmapTools.GetRoadmapProgress(App.Get<IProgressService>(), foreign, default, mentee.MentorshipId));
            await ShouldFail("roadmap.not_found", () => RoadmapTools.CreateRoadmap(App.Get<IRoadmapService>(), foreign, default, mentee.MentorshipId, "Tytuł", "Cel"));
            await ShouldFail("roadmap.not_found", () => RoadmapTools.AddRoadmapTopic(App.Get<IRoadmapService>(), foreign, default, asg.RoadmapId, asg.TopicId));
            await ShouldFail("roadmap.not_found", () => RoadmapTools.AssignExercise(App.Get<IRoadmapService>(), foreign, default, asg.RoadmapItemId, asg.ExerciseId));
            await ShouldFail("roadmap.not_found", () => RoadmapTools.ReorderRoadmapTopics(App.Get<IRoadmapService>(), foreign, default, asg.RoadmapId, [asg.RoadmapItemId]));
            await ShouldFail("mentorship.not_found", () => MentorPathResources.MenteeProfile(App.Get<IMentorshipService>(), foreign, default, mentee.MentorshipId.ToString()));
        }
    }

    [Fact]
    public async Task Agent_drafts_are_recorded_as_AI_and_cannot_be_assigned_before_publishing()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision, publish: false);
        var actor = new FixedActor(a.UserId);

        var created = await CatalogTools.CreateExerciseDraft(App.Get<IExerciseAuthoringService>(), actor, default,
            asg.TopicId, ExerciseType.QuizSingleChoice, "Quiz od agenta", "Scenariusz",
            explanationMarkdown: "Wyjaśnienie", mentorGuideMarkdown: "Guide", estimatedMinutes: 10,
            quizOptions: [new("Tak", true, "Poprawnie"), new("Nie", false, "Błędnie")]);
        var exerciseId = (Guid)created.GetType().GetProperty("exerciseId")!.GetValue(created)!;

        var exercise = await CatalogTools.GetExercise(App.Get<IExerciseAuthoringService>(), actor, default, exerciseId);
        exercise.Source.ShouldBe(ExerciseSource.AI);
        exercise.Status.ShouldBe(ExerciseStatus.Draft);

        await ShouldFail("roadmap.exercise_not_published", () => RoadmapTools.AssignExercise(App.Get<IRoadmapService>(), actor, default, asg.RoadmapItemId, exerciseId));
    }

    [Fact]
    public async Task Catalog_resource_lists_topics_with_published_and_draft_exercises()
    {
        var a = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(a, "Idempotencja API");
        var published = await Data.PublishedExerciseAsync(a, topicId, ExerciseType.PracticalTask);
        var draft = (await App.Get<IExerciseAuthoringService>().CreateDraftAsync(a.UserId,
            new(topicId, ExerciseType.QuizSingleChoice, TestDataBuilder.ValidContent(ExerciseType.QuizSingleChoice)), ExerciseSource.AI, default)).ShouldSucceed();

        var markdown = await MentorPathResources.Catalog(App.Get<ITopicService>(), new FixedActor(a.UserId), default);

        markdown.ShouldContain("## Idempotencja API");
        markdown.ShouldContain(published.ToString());
        markdown.ShouldContain(draft.ToString());
        markdown.ShouldContain("Drafts (not assignable until published)");
    }

    private static async Task ShouldFail(string code, Func<Task> call)
    {
        var ex = await Should.ThrowAsync<McpException>(call);
        ex.Message.ShouldStartWith(code + ":", customMessage: ex.Message);
    }
}
