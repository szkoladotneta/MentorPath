using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MentorPath.IntegrationTests.Database;

public sealed class TopicResourceSchemaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Theory]
    [InlineData(TopicResourceKind.Article)]
    [InlineData(TopicResourceKind.Video)]
    public async Task Resource_round_trips_at_field_limits_with_string_kind(TopicResourceKind kind)
    {
        var resource = await NewResourceAsync();
        resource.Title = new string('t', 200);
        resource.Url = "https://example.com/".PadRight(2000, 'a');
        resource.Description = new string('d', 1000);
        resource.Kind = kind;
        await InsertAsync(resource);

        var saved = await App.Db(ctx => ctx.TopicResources.AsNoTracking().SingleAsync(r => r.Id == resource.Id));
        saved.WorkspaceId.ShouldBe(resource.WorkspaceId);
        saved.TopicId.ShouldBe(resource.TopicId);
        saved.Title.ShouldBe(resource.Title);
        saved.Url.ShouldBe(resource.Url);
        saved.Description.ShouldBe(resource.Description);
        saved.Position.ShouldBe(0);
        saved.Kind.ShouldBe(kind);

        var storedKind = await App.Db(ctx => ctx.Database
            .SqlQuery<string>($"SELECT \"Kind\" AS \"Value\" FROM \"TopicResources\" WHERE \"Id\" = {resource.Id}")
            .SingleAsync());
        storedKind.ShouldBe(kind.ToString());
    }

    [Fact]
    public async Task Existing_topic_starts_without_resources_and_description_is_optional()
    {
        var resource = await NewResourceAsync();
        (await App.Db(ctx => ctx.TopicResources.AnyAsync(r => r.TopicId == resource.TopicId))).ShouldBeFalse();

        await InsertAsync(resource);

        var saved = await App.Db(ctx => ctx.TopicResources.AsNoTracking().SingleAsync(r => r.Id == resource.Id));
        saved.Description.ShouldBeNull();
    }

    [Fact]
    public async Task Resources_can_be_read_in_explicit_topic_order()
    {
        var resource = await NewResourceAsync();
        await App.Db(async ctx =>
        {
            foreach (var position in new[] { 2, 0, 1 })
            {
                ctx.TopicResources.Add(new TopicResource
                {
                    Id = Guid.CreateVersion7(), WorkspaceId = resource.WorkspaceId, TopicId = resource.TopicId,
                    Title = $"Resource {position}", Url = "https://example.com/", Kind = TopicResourceKind.Article, Position = position,
                });
            }
            return await ctx.SaveChangesAsync();
        });

        var titles = await App.Db(ctx => ctx.TopicResources.AsNoTracking()
            .Where(r => r.WorkspaceId == resource.WorkspaceId && r.TopicId == resource.TopicId)
            .OrderBy(r => r.Position).Select(r => r.Title).ToListAsync());
        titles.ShouldBe(["Resource 0", "Resource 1", "Resource 2"]);
    }

    [Fact]
    public async Task Composite_foreign_key_rejects_topic_in_another_workspace()
    {
        var resource = await NewResourceAsync();
        var foreignMentor = await Data.MentorAsync();
        resource.WorkspaceId = foreignMentor.WorkspaceId;

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(resource));

        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        postgres.ConstraintName.ShouldBe("FK_TopicResources_Topics_WorkspaceId_TopicId");
    }

    [Fact]
    public async Task Database_rejects_resource_for_missing_topic()
    {
        var resource = await NewResourceAsync();
        resource.TopicId = Guid.NewGuid();

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(resource));

        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        postgres.ConstraintName.ShouldBe("FK_TopicResources_Topics_WorkspaceId_TopicId");
    }

    [Fact]
    public async Task Database_restricts_topic_deletion_until_resources_are_removed()
    {
        var resource = await NewResourceAsync();
        await InsertAsync(resource);

        var error = await Should.ThrowAsync<PostgresException>(() => App.Db(ctx =>
            ctx.Topics.Where(t => t.Id == resource.TopicId).ExecuteDeleteAsync()));

        error.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        error.ConstraintName.ShouldBe("FK_TopicResources_Topics_WorkspaceId_TopicId");
        (await App.Db(ctx => ctx.TopicResources.AnyAsync(r => r.Id == resource.Id))).ShouldBeTrue();

        await App.Db(ctx => ctx.TopicResources.Where(r => r.Id == resource.Id).ExecuteDeleteAsync());
        (await App.Db(ctx => ctx.Topics.Where(t => t.Id == resource.TopicId).ExecuteDeleteAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Database_rejects_negative_resource_position()
    {
        var resource = await NewResourceAsync();
        resource.Position = -1;

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(resource));

        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        postgres.ConstraintName.ShouldBe("CK_TopicResources_Position");
    }

    [Theory]
    [InlineData(nameof(TopicResource.Title))]
    [InlineData(nameof(TopicResource.Url))]
    [InlineData(nameof(TopicResource.Description))]
    public async Task Database_rejects_resource_fields_exceeding_limits(string field)
    {
        var resource = await NewResourceAsync();
        switch (field)
        {
            case nameof(TopicResource.Title): resource.Title = new string('t', 201); break;
            case nameof(TopicResource.Url): resource.Url = "https://example.com/".PadRight(2001, 'a'); break;
            case nameof(TopicResource.Description): resource.Description = new string('d', 1001); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(resource));

        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    private async Task<TopicResource> NewResourceAsync()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);
        return new TopicResource
        {
            Id = Guid.CreateVersion7(), WorkspaceId = mentor.WorkspaceId, TopicId = topicId,
            Title = "Preparation", Url = "https://example.com/", Kind = TopicResourceKind.Article, Position = 0,
        };
    }

    private Task<int> InsertAsync(TopicResource resource) => App.Db(async ctx =>
    {
        ctx.TopicResources.Add(resource);
        return await ctx.SaveChangesAsync();
    });
}
