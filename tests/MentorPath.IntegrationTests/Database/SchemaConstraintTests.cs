using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Database;

public sealed class SchemaConstraintTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Migrations_have_no_pending_model_changes()
    {
        var pending = await App.Db(async ctx => (await ctx.Database.GetPendingMigrationsAsync()).ToList());
        pending.ShouldBeEmpty();
        (await App.Db(ctx => Task.FromResult(ctx.Database.HasPendingModelChanges()))).ShouldBeFalse();
    }

    [Fact]
    public async Task Database_rejects_second_correct_quiz_option()
    {
        var mentor = await Data.MentorAsync();
        var topic = await Data.TopicAsync(mentor);
        var exerciseId = await Data.PublishedExerciseAsync(mentor, topic, ExerciseType.QuizSingleChoice);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.QuizOptions.Add(new QuizOption { Id = Guid.NewGuid(), ExerciseId = exerciseId, Text = "x", IsCorrect = true, Position = 9 });
            return await ctx.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Database_rejects_second_pending_submission_for_assignment()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var a = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        await Data.DecisionSubmissionAsync(mentee, a.AssignmentId);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.Submissions.Add(new Submission
            {
                Id = Guid.NewGuid(), WorkspaceId = mentor.WorkspaceId, AssignmentId = a.AssignmentId, MenteeUserId = mentee.UserId,
                ClientRequestId = Guid.NewGuid(), AttemptNumber = 2, Status = SubmissionStatus.PendingReview, ConfidenceBefore = 3,
                SubmittedAtUtc = DateTime.UtcNow,
            });
            return await ctx.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Composite_foreign_key_rejects_exercise_pointing_to_topic_of_another_workspace()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var topicOfA = await Data.TopicAsync(mentorA);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.Exercises.Add(new Exercise
            {
                Id = Guid.NewGuid(), WorkspaceId = mentorB.WorkspaceId, TopicId = topicOfA, Type = ExerciseType.ArchitecturalDecision,
                Status = ExerciseStatus.Draft, Source = ExerciseSource.Manual, Title = "Cross", CreatedAtUtc = DateTime.UtcNow,
            });
            return await ctx.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Database_rejects_confidence_outside_range()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var a = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.Submissions.Add(new Submission
            {
                Id = Guid.NewGuid(), WorkspaceId = mentor.WorkspaceId, AssignmentId = a.AssignmentId, MenteeUserId = mentee.UserId,
                ClientRequestId = Guid.NewGuid(), AttemptNumber = 1, Status = SubmissionStatus.Incorrect, ConfidenceBefore = 6,
                SubmittedAtUtc = DateTime.UtcNow,
            });
            return await ctx.SaveChangesAsync();
        }));
    }
}
