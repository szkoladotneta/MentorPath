using MentorPath.Application.Common;
using MentorPath.Application.Feedback.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Privacy.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Cli;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Feedback;

public sealed class ProductFeedbackTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IProductFeedbackService Feedback => App.Get<IProductFeedbackService>();
    private IProductFeedbackOperatorService Operator => App.Get<IProductFeedbackOperatorService>();

    private async Task<Guid> UserWithoutWorkspaceAsync()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = $"lonely-{Guid.NewGuid():N}@test.local",
            Email = $"lonely-{Guid.NewGuid():N}@test.local",
            DisplayName = "Bez workspace'u",
            CreatedAtUtc = App.Clock.GetUtcNow().UtcDateTime,
        };
        var users = App.Get<UserManager<AppUser>>();
        (await users.CreateAsync(user, TestDataBuilder.Password)).Succeeded.ShouldBeTrue();
        await users.AddToRoleAsync(user, AppRoles.PlatformOperator);
        return user.Id;
    }

    private Task<List<ProductFeedback>> RowsAsync(Guid authorId) =>
        App.Db(ctx => ctx.ProductFeedback.AsNoTracking().Where(f => f.AuthorUserId == authorId).OrderBy(f => f.CreatedAtUtc).ToListAsync());

    [Fact]
    public async Task Mentor_and_mentee_can_submit_and_the_row_is_global_and_trimmed()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var now = App.Clock.GetUtcNow().UtcDateTime;

        (await Feedback.SubmitAsync(mentor.UserId, new("  Za dużo kliknięć  ", "/app/mentees?tab=1#x"), default)).ShouldSucceed();
        (await Feedback.SubmitAsync(mentee.UserId, new("Brakuje filtra", null), default)).ShouldSucceed();

        var m = (await RowsAsync(mentor.UserId)).ShouldHaveSingleItem();
        (m.MessageText, m.PageContext, m.CreatedAtUtc).ShouldBe(("Za dużo kliknięć", "/app/mentees", now));
        var e = (await RowsAsync(mentee.UserId)).ShouldHaveSingleItem();
        (e.MessageText, e.PageContext).ShouldBe(("Brakuje filtra", (string?)null));
    }

    [Fact]
    public async Task Mentee_with_only_a_closed_mentorship_can_still_submit()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default)).ShouldSucceed();

        (await Feedback.SubmitAsync(mentee.UserId, new("Po zamknięciu", null), default)).ShouldSucceed();

        (await RowsAsync(mentee.UserId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Actors_without_a_workspace_relationship_get_NotFound_and_nothing_is_stored()
    {
        var mentor = await Data.MentorAsync();
        var invitedOnly = await UserWithoutWorkspaceAsync();
        var unknown = Guid.NewGuid();

        (await Feedback.SubmitAsync(invitedOnly, new("Operator bez workspace'u", null), default)).ShouldBeNotFound();
        (await Feedback.SubmitAsync(unknown, new("Nieznany użytkownik", null), default)).ShouldBeNotFound();
        (await Feedback.SubmitAsync(Guid.Empty, new("Pusty identyfikator", null), default)).ShouldBeNotFound();
        (await Feedback.SubmitAsync(invitedOnly, new("", null), default)).ShouldBeNotFound();

        (await RowsAsync(invitedOnly)).ShouldBeEmpty();
        (await RowsAsync(unknown)).ShouldBeEmpty();
        (await Operator.ListAsync(500, default)).ShouldNotContain(f => f.AuthorUserId == invitedOnly || f.AuthorUserId == unknown);
        (await Feedback.SubmitAsync(mentor.UserId, new("Mentor może", null), default)).ShouldSucceed();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t")]
    public async Task Blank_message_is_rejected(string message)
    {
        var mentor = await Data.MentorAsync();

        var error = (await Feedback.SubmitAsync(mentor.UserId, new(message, null), default)).ShouldFail(ErrorKind.Validation, "feedback.invalid");

        error.FieldErrors!.ShouldContainKey("Message");
        (await RowsAsync(mentor.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Message_and_page_path_limits_are_enforced()
    {
        var mentor = await Data.MentorAsync();

        (await Feedback.SubmitAsync(mentor.UserId, new(new string('a', 2001), null), default)).ShouldFail(ErrorKind.Validation, "feedback.invalid");
        (await Feedback.SubmitAsync(mentor.UserId, new("ok", "/" + new string('p', 2000)), default))
            .ShouldFail(ErrorKind.Validation, "feedback.invalid").FieldErrors!.ShouldContainKey("PagePath");
        (await RowsAsync(mentor.UserId)).ShouldBeEmpty();

        (await Feedback.SubmitAsync(mentor.UserId, new(new string('a', 2000), "/" + new string('p', 1999)), default)).ShouldSucceed();
        (await RowsAsync(mentor.UserId)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("/app/roadmap/abc?token=secret", "/app/roadmap/abc")]
    [InlineData("/app/progress#frag", "/app/progress")]
    [InlineData("https://mentorpath.test/app/profile?x=1", "/app/profile")]
    [InlineData("?token=secret", null)]
    [InlineData("   ", null)]
    [InlineData("/app/\u001b]0;pwn\u0007x", null)]
    public async Task Page_path_never_keeps_query_string_or_fragment(string input, string? expected)
    {
        var mentor = await Data.MentorAsync();

        (await Feedback.SubmitAsync(mentor.UserId, new("Uwaga", input), default)).ShouldSucceed();

        (await RowsAsync(mentor.UserId)).ShouldHaveSingleItem().PageContext.ShouldBe(expected);
    }

    [Fact]
    public async Task Operator_list_is_newest_first_with_author_ids_and_clamped_limit()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await Feedback.SubmitAsync(mentor.UserId, new("Pierwsza", "/app/a"), default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromMinutes(1));
        (await Feedback.SubmitAsync(mentee.UserId, new("Druga", null), default)).ShouldSucceed();

        var all = (await Operator.ListAsync(500, default)).Where(f => f.AuthorUserId == mentor.UserId || f.AuthorUserId == mentee.UserId);
        all.Select(f => (f.AuthorUserId, f.Message, f.PagePath)).ShouldBe(
            [(mentee.UserId, "Druga", (string?)null), (mentor.UserId, "Pierwsza", "/app/a")]);
        (await Operator.ListAsync(1, default)).ShouldHaveSingleItem().AuthorUserId.ShouldBe(mentee.UserId);
        (await Operator.ListAsync(0, default)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task List_feedback_command_prints_items_and_rejects_invalid_limit()
    {
        var mentor = await Data.MentorAsync();
        (await Feedback.SubmitAsync(mentor.UserId, new("Linia 1\nLinia 2", "/app/x"), default)).ShouldSucceed();

        await using var scope = App.CreateScope();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await FeedbackCommands.ListAsync(scope.ServiceProvider, new Dictionary<string, string>(), stdout, stderr, default);

        code.ShouldBe(0, stderr.ToString());
        stdout.ToString().ShouldContain($"AuthorUserId={mentor.UserId} Page=/app/x");
        stdout.ToString().ShouldContain("  Linia 1\n  Linia 2");
        stdout.ToString().ShouldContain(" feedback item(s).");

        (await Feedback.SubmitAsync(mentor.UserId, new("Zła \u001b[31mczerwień\u001b]0;tytuł\u0007", null), default)).ShouldSucceed();
        var escaped = new StringWriter();
        await FeedbackCommands.ListAsync(scope.ServiceProvider, new Dictionary<string, string>(), escaped, new StringWriter(), default);
        escaped.ToString().ShouldNotContain("\u001b");
        escaped.ToString().ShouldNotContain("\u0007");

        foreach (var bad in new[] { "0", "501", "abc", "-1" })
        {
            var err = new StringWriter();
            (await FeedbackCommands.ListAsync(scope.ServiceProvider,
                new Dictionary<string, string> { ["limit"] = bad }, new StringWriter(), err, default)).ShouldBe(2);
            err.ToString().ShouldContain("Usage: list-feedback");
        }
    }

    [Fact]
    public async Task Export_contains_only_own_feedback_and_anonymization_blanks_only_own_messages()
    {
        var mentor = await Data.MentorAsync();
        var a = await Data.MenteeAsync(mentor);
        var b = await Data.MenteeAsync(mentor);
        (await Feedback.SubmitAsync(a.UserId, new("Uwaga A sekret", "/app/a"), default)).ShouldSucceed();
        (await Feedback.SubmitAsync(b.UserId, new("Uwaga B", null), default)).ShouldSucceed();
        var privacy = App.Get<IPersonalDataService>();

        var export = (await privacy.ExportAsync(new(a.UserId, null), default)).ShouldSucceed();
        var exported = export.ProductFeedback.ShouldHaveSingleItem();
        (exported.Message, exported.PagePath).ShouldBe(("Uwaga A sekret", "/app/a"));

        var dry = (await privacy.AnonymizeAsync(new(a.UserId, null), apply: false, default)).ShouldSucceed();
        dry.ProductFeedbackScrubbed.ShouldBe(1);
        (await RowsAsync(a.UserId)).ShouldHaveSingleItem().MessageText.ShouldBe("Uwaga A sekret");

        (await privacy.AnonymizeAsync(new(a.UserId, null), apply: true, default)).ShouldSucceed().ProductFeedbackScrubbed.ShouldBe(1);

        var kept = (await RowsAsync(a.UserId)).ShouldHaveSingleItem();
        (kept.MessageText, kept.AuthorUserId).ShouldBe(("", a.UserId));
        (await RowsAsync(b.UserId)).ShouldHaveSingleItem().MessageText.ShouldBe("Uwaga B");
        (await Operator.ListAsync(500, default)).Where(f => f.AuthorUserId == a.UserId || f.AuthorUserId == b.UserId)
            .Select(f => f.Message).ShouldBe(["Uwaga B", ""], ignoreOrder: true);
    }
}
