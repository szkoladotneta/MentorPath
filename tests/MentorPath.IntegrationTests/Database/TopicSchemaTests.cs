using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Database;

public sealed class TopicSchemaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Why_it_matters_is_nullable_for_existing_topics_and_stores_markdown()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);

        var existingTopic = await App.Db(ctx => ctx.Topics.AsNoTracking().SingleAsync(t => t.Id == topicId));
        existingTopic.WhyItMattersMarkdown.ShouldBeNull();

        const string rationale = "## Dlaczego to ważne\n\nPrzygotowuje do podejmowania decyzji.";
        await App.Db(ctx => ctx.Topics.Where(t => t.Id == topicId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.WhyItMattersMarkdown, rationale)));

        var savedTopic = await App.Db(ctx => ctx.Topics.AsNoTracking().SingleAsync(t => t.Id == topicId));
        savedTopic.WhyItMattersMarkdown.ShouldBe(rationale);
    }
}
