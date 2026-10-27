using MentorPath.Data.Entities;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MentorPath.IntegrationTests.Database;

public sealed class ProductFeedbackSchemaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Product_feedback_round_trips_limits_and_redacted_empty_value()
    {
        var mentor = await Data.MentorAsync();
        var feedback = new ProductFeedback
        {
            Id = Guid.CreateVersion7(),
            AuthorUserId = mentor.UserId,
            MessageText = new string('m', 2000),
            PageContext = new string('p', 2000),
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        var redacted = NewFeedback(mentor.UserId);
        redacted.MessageText = "";

        await App.Db(async ctx =>
        {
            ctx.ProductFeedback.AddRange(feedback, redacted);
            return await ctx.SaveChangesAsync();
        });

        var saved = await App.Db(ctx => ctx.ProductFeedback.AsNoTracking().SingleAsync(x => x.Id == feedback.Id));
        saved.AuthorUserId.ShouldBe(mentor.UserId);
        saved.MessageText.ShouldBe(feedback.MessageText);
        saved.PageContext.ShouldBe(feedback.PageContext);
        saved.CreatedAtUtc.ShouldBe(feedback.CreatedAtUtc);

        var savedRedacted = await App.Db(ctx => ctx.ProductFeedback.AsNoTracking().SingleAsync(x => x.Id == redacted.Id));
        savedRedacted.MessageText.ShouldBeEmpty();
        savedRedacted.PageContext.ShouldBeNull();
    }

    [Fact]
    public async Task Database_rejects_product_feedback_text_over_2000_characters()
    {
        var mentor = await Data.MentorAsync();
        var feedback = NewFeedback(mentor.UserId);
        feedback.MessageText = new string('m', 2001);

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(feedback));

        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task Database_requires_product_feedback_message_text()
    {
        var mentor = await Data.MentorAsync();
        var id = Guid.CreateVersion7();
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var error = await Should.ThrowAsync<PostgresException>(() => App.Db(ctx =>
            ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"ProductFeedback\" (\"Id\", \"AuthorUserId\", \"MessageText\", \"PageContext\", \"CreatedAtUtc\") VALUES ({id}, {mentor.UserId}, NULL, NULL, {createdAt})")));

        error.SqlState.ShouldBe(PostgresErrorCodes.NotNullViolation);
    }

    [Fact]
    public async Task Database_rejects_product_feedback_page_context_over_2000_characters()
    {
        var mentor = await Data.MentorAsync();
        var feedback = NewFeedback(mentor.UserId);
        feedback.PageContext = new string('p', 2001);

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(feedback));

        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task Database_rejects_product_feedback_for_unknown_author()
    {
        var feedback = NewFeedback(Guid.NewGuid());

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertAsync(feedback));

        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        postgres.ConstraintName.ShouldBe("FK_ProductFeedback_AspNetUsers_AuthorUserId");
    }

    private static ProductFeedback NewFeedback(Guid authorUserId) => new()
    {
        Id = Guid.CreateVersion7(),
        AuthorUserId = authorUserId,
        MessageText = new string('m', 2000),
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private Task<int> InsertAsync(ProductFeedback feedback) => App.Db(async ctx =>
    {
        ctx.ProductFeedback.Add(feedback);
        return await ctx.SaveChangesAsync();
    });
}
