using MentorPath.Data.Entities;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Database;

public sealed class CompetencyPrerequisiteSchemaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Prerequisite_relationship_requires_topics_from_the_same_workspace()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var topicA = await Data.TopicAsync(mentorA);
        var prerequisiteA = await Data.TopicAsync(mentorA);
        var topicB = await Data.TopicAsync(mentorB);

        await App.Db(async ctx =>
        {
            ctx.CompetencyPrerequisites.Add(new CompetencyPrerequisite
            {
                WorkspaceId = mentorA.WorkspaceId,
                TopicId = topicA,
                PrerequisiteTopicId = prerequisiteA,
            });
            return await ctx.SaveChangesAsync();
        });

        var saved = await App.Db(ctx => ctx.CompetencyPrerequisites.AsNoTracking()
            .SingleAsync(p => p.TopicId == topicA && p.PrerequisiteTopicId == prerequisiteA));
        saved.WorkspaceId.ShouldBe(mentorA.WorkspaceId);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.CompetencyPrerequisites.Add(new CompetencyPrerequisite
            {
                WorkspaceId = mentorA.WorkspaceId,
                TopicId = topicA,
                PrerequisiteTopicId = topicB,
            });
            return await ctx.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Database_rejects_a_topic_as_its_own_prerequisite()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);

        await Should.ThrowAsync<DbUpdateException>(() => App.Db(async ctx =>
        {
            ctx.CompetencyPrerequisites.Add(new CompetencyPrerequisite
            {
                WorkspaceId = mentor.WorkspaceId,
                TopicId = topicId,
                PrerequisiteTopicId = topicId,
            });
            return await ctx.SaveChangesAsync();
        }));
    }
}
