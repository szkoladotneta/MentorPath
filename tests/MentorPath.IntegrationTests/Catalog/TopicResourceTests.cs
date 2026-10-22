using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Catalog;

public sealed class TopicResourceTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private ITopicResourceService Resources => App.Get<ITopicResourceService>();

    [Fact]
    public async Task Owner_can_read_and_atomically_replace_an_ordered_resource_list()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);

        (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().ShouldBeEmpty();
        (await Resources.ReplaceAsync(mentor.UserId, topicId,
        [
            new("Artykuł", "https://example.com/article", TopicResourceKind.Article, "Opis"),
            new("Wideo", "https://youtu.be/dQw4w9WgXcQ", TopicResourceKind.Video, null),
        ], default)).ShouldSucceed();

        var saved = (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        saved.Select(r => r.Title).ShouldBe(["Artykuł", "Wideo"]);
        saved.Select(r => r.Position).ShouldBe([0, 1]);
        saved[0].Description.ShouldBe("Opis");
        saved[1].Description.ShouldBeNull();

        (await Resources.ReplaceAsync(mentor.UserId, topicId, [], default)).ShouldSucceed();
        (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().ShouldBeEmpty();
    }

    [Fact]
    public async Task Resource_operations_are_isolated_from_other_mentors_mentees_and_unknown_users()
    {
        var mentor = await Data.MentorAsync();
        var foreignMentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var sameWorkspaceMentee = await Data.MenteeAsync(mentor);
        var foreignMentee = await Data.MenteeAsync(foreignMentor);
        var topicId = await Data.TopicAsync(mentor);
        var input = new[] { new TopicResourceInput("Artykuł", "https://example.com", TopicResourceKind.Article, null) };
        var unknownUserId = Guid.NewGuid();
        var missingTopicId = Guid.NewGuid();

        foreach (var actorId in new[]
                 {
                     foreignMentor.UserId,
                     mentee.UserId,
                     sameWorkspaceMentee.UserId,
                     foreignMentee.UserId,
                     unknownUserId,
                 })
        {
            (await Resources.GetAsync(actorId, topicId, default)).ShouldBeNotFound();
            (await Resources.ReplaceAsync(actorId, topicId, input, default)).ShouldBeNotFound();
        }

        (await Resources.GetAsync(mentor.UserId, missingTopicId, default)).ShouldBeNotFound();
        (await Resources.ReplaceAsync(mentor.UserId, missingTopicId, input, default)).ShouldBeNotFound();
        (await Resources.ReplaceAsync(mentor.UserId, missingTopicId,
            [new("", "http://not-https.example", (TopicResourceKind)99, null)], default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Invalid_replacement_leaves_the_previous_resource_list_unchanged()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);
        (await Resources.ReplaceAsync(mentor.UserId, topicId,
            [new("Poprawny materiał", "https://example.com", TopicResourceKind.Article, null)], default)).ShouldSucceed();

        var error = (await Resources.ReplaceAsync(mentor.UserId, topicId,
        [
            new(new string('t', 201), "https://user:password@example.com", (TopicResourceKind)99, "<b>HTML</b>"),
        ], default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_resources");
        error.FieldErrors!.Keys.ShouldContain("Resources[0].Title");
        error.FieldErrors.Keys.ShouldContain("Resources[0].Url");
        error.FieldErrors.Keys.ShouldContain("Resources[0].Kind");
        error.FieldErrors.Keys.ShouldContain("Resources[0].Description");

        var saved = (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed();
        saved.Select(r => r.Title).ShouldBe(["Poprawny materiał"]);
    }

    [Fact]
    public async Task Resource_fields_accept_exact_limits_and_reject_oversized_values()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);
        var maximumLengthUrl = "https://example.com/" + new string('a', 1980);
        var title = new string('t', 200);
        var description = new string('d', 1000);

        (await Resources.ReplaceAsync(mentor.UserId, topicId,
            [new(title, maximumLengthUrl, TopicResourceKind.Article, description)], default)).ShouldSucceed();
        var saved = (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed().Single();
        saved.Title.Length.ShouldBe(200);
        saved.Url.Length.ShouldBe(2000);
        saved.Description!.Length.ShouldBe(1000);

        var error = (await Resources.ReplaceAsync(mentor.UserId, topicId,
        [
            new("title", "http://example.com", TopicResourceKind.Article, null),
            new("title", "https://example.com/" + new string('a', 1981), TopicResourceKind.Article, new string('d', 1001)),
        ], default)).ShouldFail(ErrorKind.Validation, "catalog.invalid_resources");
        error.FieldErrors!.Keys.ShouldContain("Resources[0].Url");
        error.FieldErrors.Keys.ShouldContain("Resources[1].Url");
        error.FieldErrors.Keys.ShouldContain("Resources[1].Description");
    }

    [Fact]
    public async Task Concurrent_replacements_are_serialized_as_whole_lists()
    {
        var mentor = await Data.MentorAsync();
        var topicId = await Data.TopicAsync(mentor);
        TopicResourceInput[] left = [new("Lewy A", "https://example.com/a", TopicResourceKind.Article, null), new("Lewy B", "https://example.com/b", TopicResourceKind.Article, null)];
        TopicResourceInput[] right = [new("Prawy", "https://example.com/c", TopicResourceKind.Video, null)];

        var results = await Task.WhenAll(
            Resources.ReplaceAsync(mentor.UserId, topicId, left, default),
            Resources.ReplaceAsync(mentor.UserId, topicId, right, default));

        results.ShouldAllBe(r => r.IsSuccess);
        var finalTitles = (await Resources.GetAsync(mentor.UserId, topicId, default)).ShouldSucceed()
            .Select(r => r.Title).ToArray();
        (finalTitles.SequenceEqual(["Lewy A", "Lewy B"]) || finalTitles.SequenceEqual(["Prawy"])).ShouldBeTrue();
    }
}
