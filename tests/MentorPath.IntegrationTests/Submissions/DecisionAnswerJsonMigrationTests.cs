using System.Text.Json;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Submissions;

public sealed class DecisionAnswerJsonMigrationTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private const string MigrationId = "20261028134411_MigrateDecisionAnswerJsonV2";

    [Fact]
    public async Task V1_json_is_migrated_without_changing_values_and_other_documents_are_untouched()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var v1Assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var nullAssignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var otherVersionAssignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var v1 = await Data.DecisionSubmissionAsync(mentee, v1Assignment.AssignmentId);
        var nullAnswer = await Data.DecisionSubmissionAsync(mentee, nullAssignment.AssignmentId);
        var otherVersion = await Data.DecisionSubmissionAsync(mentee, otherVersionAssignment.AssignmentId);
        const string v1Json = """{"schemaVersion":1,"decision":"Wybór","rationale":"Uzasadnienie","alternatives":"","tradeOffs":"Kompromis","risks":"Ryzyko","reconsiderationConditions":"Warunek"}""";
        const string otherVersionJson = """{"schemaVersion":99,"answer":{"decision":"Zachowaj"}}""";

        await App.Db(async ctx =>
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Submissions\" SET \"DecisionAnswerJson\" = CAST({v1Json} AS jsonb) WHERE \"Id\" = {v1.SubmissionId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Submissions\" SET \"DecisionAnswerJson\" = NULL WHERE \"Id\" = {nullAnswer.SubmissionId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Submissions\" SET \"DecisionAnswerJson\" = CAST({otherVersionJson} AS jsonb) WHERE \"Id\" = {otherVersion.SubmissionId}");
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {MigrationId}");
            await ctx.Database.MigrateAsync();
            return 0;
        });

        var documents = await App.Db(ctx => ctx.Submissions
            .Where(s => s.Id == v1.SubmissionId || s.Id == nullAnswer.SubmissionId || s.Id == otherVersion.SubmissionId)
            .ToDictionaryAsync(s => s.Id, s => s.DecisionAnswerJson));
        using var migrated = JsonDocument.Parse(documents[v1.SubmissionId]!);
        migrated.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(2);
        var answer = migrated.RootElement.GetProperty("answer");
        answer.GetProperty("decision").GetString().ShouldBe("Wybór");
        answer.GetProperty("rationale").GetString().ShouldBe("Uzasadnienie");
        answer.GetProperty("alternatives").GetString().ShouldBe("");
        answer.GetProperty("tradeOffs").GetString().ShouldBe("Kompromis");
        answer.GetProperty("risks").GetString().ShouldBe("Ryzyko");
        answer.GetProperty("reconsiderationConditions").GetString().ShouldBe("Warunek");
        documents[nullAnswer.SubmissionId].ShouldBeNull();
        using var unchanged = JsonDocument.Parse(documents[otherVersion.SubmissionId]!);
        unchanged.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(99);
        unchanged.RootElement.GetProperty("answer").GetProperty("decision").GetString().ShouldBe("Zachowaj");
    }
}
