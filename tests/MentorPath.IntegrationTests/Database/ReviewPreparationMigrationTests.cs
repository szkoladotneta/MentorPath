using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace MentorPath.IntegrationTests.Database;

public sealed class ReviewPreparationMigrationTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private const string MigrationId = "20261103124004_AddMentorshipReviewConsentAndDrafts";

    [Fact]
    public async Task Upgrade_preserves_existing_answers_feedback_and_statuses_without_implicit_consent()
    {
        // Use an isolated database so migrating an older populated schema cannot affect other tests.
        var databaseName = $"review_upgrade_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(Database.ConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(Database.ConnectionString)
            {
                Database = databaseName, Pooling = false,
            };
            await using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrations = ctx.Database.GetMigrations().ToList();
            migrations.Last().ShouldBe(MigrationId);
            await ctx.GetService<IMigrator>().MigrateAsync(migrations[^2]);

            var now = App.Clock.GetUtcNow().UtcDateTime;
            var mentorId = Guid.CreateVersion7();
            var menteeId = Guid.CreateVersion7();
            var workspaceId = Guid.CreateVersion7();
            var mentorshipId = Guid.CreateVersion7();
            var topicId = Guid.CreateVersion7();
            var exerciseId = Guid.CreateVersion7();
            var roadmapId = Guid.CreateVersion7();
            var itemId = Guid.CreateVersion7();
            var assignmentId = Guid.CreateVersion7();
            var reviewedId = Guid.CreateVersion7();
            var pendingId = Guid.CreateVersion7();
            const string answer = """{"schemaVersion":2,"answer":{"decision":"Existing decision","rationale":"Existing rationale"}}""";
            ctx.Users.AddRange(
                new AppUser { Id = mentorId, DisplayName = "Mentor", CreatedAtUtc = now },
                new AppUser { Id = menteeId, DisplayName = "Mentee", CreatedAtUtc = now });
            ctx.Workspaces.Add(new Workspace { Id = workspaceId, OwnerUserId = mentorId, Name = "Existing workspace", CreatedAtUtc = now });
            ctx.Mentorships.Add(new Mentorship
            {
                Id = mentorshipId, WorkspaceId = workspaceId, MenteeUserId = menteeId,
                Status = MentorshipStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now,
            });
            ctx.Topics.Add(new Topic { Id = topicId, WorkspaceId = workspaceId, Slug = "existing", Title = "Existing", CreatedAtUtc = now });
            ctx.Exercises.Add(new Exercise
            {
                Id = exerciseId, WorkspaceId = workspaceId, TopicId = topicId, Title = "Existing exercise",
                Type = ExerciseType.ArchitecturalDecision, Status = ExerciseStatus.Published, CreatedAtUtc = now,
            });
            ctx.Roadmaps.Add(new Roadmap
            {
                Id = roadmapId, WorkspaceId = workspaceId, MentorshipId = mentorshipId,
                Title = "Existing roadmap", Status = RoadmapStatus.Published, CreatedAtUtc = now,
            });
            ctx.RoadmapItems.Add(new RoadmapItem { Id = itemId, WorkspaceId = workspaceId, RoadmapId = roadmapId, TopicId = topicId, IsActive = true });
            ctx.Assignments.Add(new Assignment { Id = assignmentId, WorkspaceId = workspaceId, RoadmapItemId = itemId, ExerciseId = exerciseId, IsActive = true });
            ctx.Submissions.AddRange(
                new Submission
                {
                    Id = reviewedId, WorkspaceId = workspaceId, AssignmentId = assignmentId, MenteeUserId = menteeId,
                    ClientRequestId = Guid.CreateVersion7(), AttemptNumber = 1, Status = SubmissionStatus.NeedsRevision,
                    DecisionAnswerJson = answer, ConfidenceBefore = 3, SubmittedAtUtc = now,
                },
                new Submission
                {
                    Id = pendingId, WorkspaceId = workspaceId, AssignmentId = assignmentId, MenteeUserId = menteeId,
                    ClientRequestId = Guid.CreateVersion7(), AttemptNumber = 2, Status = SubmissionStatus.PendingReview,
                    DecisionAnswerJson = answer, ConfidenceBefore = 4, SubmittedAtUtc = now,
                });
            ctx.MentorFeedback.Add(new MentorFeedback
            {
                Id = Guid.CreateVersion7(), WorkspaceId = workspaceId, SubmissionId = reviewedId, MentorUserId = mentorId,
                Verdict = ReviewVerdict.NeedsRevision, StrengthsMarkdown = "Existing strengths",
                ImprovementsMarkdown = "Existing improvements", ChallengeQuestionMarkdown = "Existing question", CreatedAtUtc = now,
            });
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            var beforeSubmissions = await ctx.Submissions.AsNoTracking().OrderBy(s => s.AttemptNumber).ToListAsync();
            var beforeFeedback = await ctx.MentorFeedback.AsNoTracking().SingleAsync();

            await ctx.Database.MigrateAsync();

            (await ctx.MentorshipReviewConsents.CountAsync()).ShouldBe(0);
            (await ctx.ReviewDrafts.CountAsync()).ShouldBe(0);
            (await ctx.ReviewDraftCriterionScores.CountAsync()).ShouldBe(0);
            var mentorship = await ctx.Mentorships.AsNoTracking().SingleAsync();
            mentorship.Id.ShouldBe(mentorshipId);
            mentorship.Status.ShouldBe(MentorshipStatus.Active);
            var afterSubmissions = await ctx.Submissions.AsNoTracking().OrderBy(s => s.AttemptNumber).ToListAsync();
            afterSubmissions.Count.ShouldBe(2);
            for (var index = 0; index < beforeSubmissions.Count; index++)
            {
                var before = beforeSubmissions[index];
                var after = afterSubmissions[index];
                after.Id.ShouldBe(before.Id);
                after.Status.ShouldBe(before.Status);
                after.DecisionAnswerJson.ShouldBe(before.DecisionAnswerJson);
                after.ConfidenceBefore.ShouldBe(before.ConfidenceBefore);
                after.SubmittedAtUtc.ShouldBe(before.SubmittedAtUtc);
                after.ClientRequestId.ShouldBe(before.ClientRequestId);
            }
            var afterFeedback = await ctx.MentorFeedback.AsNoTracking().SingleAsync();
            afterFeedback.Id.ShouldBe(beforeFeedback.Id);
            afterFeedback.Verdict.ShouldBe(beforeFeedback.Verdict);
            afterFeedback.StrengthsMarkdown.ShouldBe(beforeFeedback.StrengthsMarkdown);
            afterFeedback.ImprovementsMarkdown.ShouldBe(beforeFeedback.ImprovementsMarkdown);
            afterFeedback.ChallengeQuestionMarkdown.ShouldBe(beforeFeedback.ChallengeQuestionMarkdown);
            afterFeedback.CreatedAtUtc.ShouldBe(beforeFeedback.CreatedAtUtc);
            (await ctx.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            ctx.Database.HasPendingModelChanges().ShouldBeFalse();
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
