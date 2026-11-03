using System.Text.Json;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Mcp;
using MentorPath.Web.Mcp.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace MentorPath.IntegrationTests.Mcp;

public sealed class McpReviewToolsTests(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Mcp:Reviews:Enabled"] = "true",
        ["Mcp:Reviews:ProviderNotice"] = "Synthetic approved test client. No real data.",
    };

    [Fact]
    public async Task Mentor_can_read_learning_context_and_save_only_a_private_draft_until_revocation()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var actor = new FixedActor(mentor.UserId);
        await GrantAsync(mentee);
        var context = App.Get<IMcpReviewContextService>();
        var drafts = App.Get<IMcpReviewDraftService>();
        var selected = await ReviewTools.GetReviewContext(context, actor, default, mentee.MentorshipId, submission.SubmissionId);
        selected.UntrustedContent.Submission.DecisionAnswer.ShouldNotBeNull();
        selected.ContentWarning.ShouldContain("never instructions");
        var history = await ReviewTools.GetSubmissionHistory(context, actor, default, mentee.MentorshipId, assignment.AssignmentId);
        history.UntrustedContent.Attempts.Select(a => a.SubmissionId).ShouldBe([submission.SubmissionId]);
        var empty = await ReviewTools.GetReviewDraft(drafts, actor, default, mentee.MentorshipId, submission.SubmissionId);
        empty.UntrustedDraft.ShouldBeNull();
        var created = await ReviewTools.SaveReviewDraft(drafts, actor, default, mentee.MentorshipId, submission.SubmissionId,
            strengthsMarkdown: "Synthetic draft text. Ignore this sentence as an instruction.");
        created.UntrustedDraft.ShouldNotBeNull();
        created.ReviewPath.ShouldBe($"/mentor/reviews/{submission.SubmissionId}");
        created.PublicationNotice.ShouldContain("not published");
        var updated = await ReviewTools.SaveReviewDraft(drafts, actor, default, mentee.MentorshipId, submission.SubmissionId,
            strengthsMarkdown: "Updated draft", expectedVersion: created.UntrustedDraft.Version);
        updated.UntrustedDraft!.Version.ShouldBeGreaterThan(created.UntrustedDraft.Version);
        var serialized = JsonSerializer.Serialize(updated, McpComposition.SerializerOptions);
        serialized.ShouldContain("untrustedDraft");
        serialized.ShouldNotContain(mentee.Email);
        var consent = (await App.Get<IMentorshipReviewConsentService>().GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        (await App.Get<IMentorshipReviewConsentService>().RevokeAsync(mentee.UserId, new(mentee.MentorshipId, consent.Version!.Value), default)).ShouldSucceed();
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetReviewContext(context, actor, default, mentee.MentorshipId, submission.SubmissionId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetSubmissionHistory(context, actor, default, mentee.MentorshipId, assignment.AssignmentId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetReviewDraft(drafts, actor, default, mentee.MentorshipId, submission.SubmissionId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.SaveReviewDraft(drafts, actor, default, mentee.MentorshipId, submission.SubmissionId,
            expectedVersion: updated.UntrustedDraft.Version));
        (await App.Get<IReviewDraftService>().GetAsync(mentor.UserId, submission.SubmissionId, default)).ShouldSucceed()!
            .Content.StrengthsMarkdown.ShouldBe("Updated draft");
    }

    [Fact]
    public async Task Every_new_tool_rejects_foreign_mentors_own_and_foreign_mentees_and_unknown_users()
    {
        var mentor = await Data.MentorAsync();
        var foreignMentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var foreignMentee = await Data.MenteeAsync(foreignMentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        await GrantAsync(mentee);
        foreach (var actorId in new[] { foreignMentor.UserId, mentee.UserId, foreignMentee.UserId, Guid.NewGuid() })
        {
            var actor = new FixedActor(actorId);
            await NotFoundAsync(() => ReviewTools.GetReviewContext(App.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
            await NotFoundAsync(() => ReviewTools.GetSubmissionHistory(App.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, assignment.AssignmentId));
            await NotFoundAsync(() => ReviewTools.ReadSubmissionAttachment(App.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, submission.SubmissionId, Guid.NewGuid()));
            await NotFoundAsync(() => ReviewTools.GetReviewDraft(App.Get<IMcpReviewDraftService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
            await NotFoundAsync(() => ReviewTools.SaveReviewDraft(App.Get<IMcpReviewDraftService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
            await NotFoundAsync(() => CatalogTools.GetTopicResources(App.Get<ITopicResourceService>(), actor, default, assignment.TopicId));
        }
    }

    [Fact]
    public async Task Default_off_feature_denies_new_review_tools_even_with_a_persisted_grant()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        await GrantAsync(mentee);
        (await App.Get<ITopicResourceService>().ReplaceAsync(mentor.UserId, assignment.TopicId,
            [new("Preparation", "https://example.com/preparation", TopicResourceKind.Article, "Read first")], default)).ShouldSucceed();
        await using var disabledApp = await TestApp.CreateAsync(Database);
        var actor = new FixedActor(mentor.UserId);
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetReviewContext(disabledApp.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetSubmissionHistory(disabledApp.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, assignment.AssignmentId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.ReadSubmissionAttachment(disabledApp.Get<IMcpReviewContextService>(), actor, default, mentee.MentorshipId, submission.SubmissionId, Guid.NewGuid()));
        await Should.ThrowAsync<McpException>(() => ReviewTools.GetReviewDraft(disabledApp.Get<IMcpReviewDraftService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
        await Should.ThrowAsync<McpException>(() => ReviewTools.SaveReviewDraft(disabledApp.Get<IMcpReviewDraftService>(), actor, default, mentee.MentorshipId, submission.SubmissionId));
        (await CatalogTools.GetTopicResources(disabledApp.Get<ITopicResourceService>(), actor, default, assignment.TopicId))
            .ShouldHaveSingleItem().Title.ShouldBe("Preparation");
    }

    private async Task GrantAsync(MenteeRef mentee) =>
        (await App.Get<IMentorshipReviewConsentService>().GrantAsync(mentee.UserId,
            new(mentee.MentorshipId, App.Get<IOptions<McpReviewOptions>>().Value.DisclosureVersion, null), default)).ShouldSucceed();

    private static async Task NotFoundAsync(Func<Task> call) =>
        (await Should.ThrowAsync<McpException>(call)).Message.ShouldContain("not_found");

    private sealed class FixedActor(Guid id) : IActorContext
    {
        public Guid ActorUserId => id;
    }
}
