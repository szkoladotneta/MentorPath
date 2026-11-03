using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MentorPath.IntegrationTests.Reviews;

public sealed class McpReviewContextTests(PostgresFixture db) : ReviewFeatureTestBase(db)
{
    [Fact]
    public async Task Context_is_a_pure_read_with_full_exercise_selected_rubric_and_structured_answer()
    {
        var item = await PendingAsync();
        var before = await App.Db(d => d.ActivityEvents.CountAsync(e => e.SubmissionId == item.SubmissionId));
        var context = (await App.Get<IMcpReviewContextService>()
            .GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldSucceed();
        context.MentorshipId.ShouldBe(item.Mentee.MentorshipId);
        context.AssignmentId.ShouldBe(item.Assignment.AssignmentId);
        context.Exercise.Content.MentorGuideMarkdown.ShouldBe("Guide AD");
        context.Exercise.Content.ScenarioMarkdown.ShouldBe("Scenariusz decyzji");
        context.Criteria.Select(c => c.CriterionId).ShouldBe(item.Criteria.Take(2).Select(c => c.CriterionId));
        context.Criteria.ShouldAllBe(c => c.LevelDescriptions.Count == 5);
        context.Submission.DecisionAnswer!.Decision.ShouldBe("Modularny monolit");
        context.Submission.DecisionAnswer.ReconsiderationConditions.ShouldBe("Gdy zespół > 3");
        context.Submission.ConfidenceBefore.ShouldBe(3);
        context.Submission.Feedback.ShouldBeNull();
        context.Status.ShouldBe(SubmissionStatus.PendingReview);
        (await App.Db(d => d.ActivityEvents.CountAsync(e => e.SubmissionId == item.SubmissionId))).ShouldBe(before);
    }

    [Fact]
    public async Task History_pages_descend_with_feedback_scores_reflections_and_preserve_inactive_history()
    {
        var item = await PendingAsync();
        var content = Complete(item, ReviewVerdict.NeedsRevision);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(item.Mentor.UserId,
            new(item.SubmissionId, content.StrengthsMarkdown!, content.ImprovementsMarkdown!,
                content.ChallengeQuestionMarkdown!, content.Verdict!.Value, content.Scores), default)).ShouldSucceed();
        (await App.Get<IReflectionService>().SaveReflectionAsync(item.Mentee.UserId, new(item.SubmissionId, 5, "Refleksja"), default)).ShouldSucceed();
        var second = await Data.DecisionSubmissionAsync(item.Mentee, item.Assignment.AssignmentId);
        (await App.Get<IRubricService>().SetActiveAsync(item.Mentor.UserId, item.Criteria[0].CriterionId, false, default)).ShouldSucceed();
        await App.Db(async d =>
        {
            await d.Assignments.Where(a => a.Id == item.Assignment.AssignmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false));
            await d.RoadmapItems.Where(a => a.Id == item.Assignment.RoadmapItemId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false));
            return 0;
        });
        var service = App.Get<IMcpReviewContextService>();
        var firstPage = (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, null, 1, default)).ShouldSucceed();
        firstPage.Attempts.Single().SubmissionId.ShouldBe(second.SubmissionId);
        firstPage.NextBeforeAttemptNumber.ShouldBe(2);
        var lastPage = (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, firstPage.NextBeforeAttemptNumber, 1, default)).ShouldSucceed();
        lastPage.NextBeforeAttemptNumber.ShouldBeNull();
        var historical = lastPage.Attempts.Single();
        historical.SubmissionId.ShouldBe(item.SubmissionId);
        historical.Feedback!.Scores!.Count.ShouldBe(2);
        historical.Feedback.Scores.Single(s => s.CriterionId == item.Criteria[0].CriterionId).Score.ShouldBe(4);
        historical.Feedback.Scores.ShouldAllBe(s => s.LevelDescription.Length > 0);
        historical.Reflection!.TakeawayMarkdown.ShouldBe("Refleksja");
        var context = (await service.GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldSucceed();
        context.Criteria.Select(c => c.CriterionId).ShouldBe([item.Criteria[1].CriterionId]);
        context.Submission.Feedback!.Scores!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task History_default_and_maximum_page_sizes_are_bounded_and_invalid_cursors_fail()
    {
        var item = await PendingAsync();
        await App.Db(async d =>
        {
            for (var number = 2; number <= 105; number++)
                d.Submissions.Add(new Submission
                {
                    Id = Guid.NewGuid(), WorkspaceId = item.Mentor.WorkspaceId, AssignmentId = item.Assignment.AssignmentId,
                    MenteeUserId = item.Mentee.UserId, ClientRequestId = Guid.NewGuid(), AttemptNumber = number,
                    ConfidenceBefore = 3, SubmittedAtUtc = App.Clock.GetUtcNow().UtcDateTime, Status = SubmissionStatus.NeedsRevision,
                });
            return await d.SaveChangesAsync();
        });
        var service = App.Get<IMcpReviewContextService>();
        var defaults = (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, null, 0, default)).ShouldSucceed();
        defaults.Attempts.Count.ShouldBe(McpReviewOptions.DefaultHistoryPageSize);
        defaults.Attempts.First().AttemptNumber.ShouldBe(105);
        defaults.NextBeforeAttemptNumber.ShouldBe(86);
        var maximum = (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, null, McpReviewOptions.MaxHistoryPageSize, default)).ShouldSucceed();
        maximum.Attempts.Count.ShouldBe(100);
        foreach (var size in new[] { -1, 101 })
            (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
                item.Assignment.AssignmentId, null, size, default)).ShouldFail(ErrorKind.Validation, "mcp_review.invalid_page");
        (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, 0, 20, default)).ShouldFail(ErrorKind.Validation, "mcp_review.invalid_page");
        (await service.GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, 1, 20, default)).ShouldSucceed().Attempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Attachment_chunks_are_bounded_bytes_not_storage_keys_and_do_not_fetch_external_URLs()
    {
        var attachment = await PracticalAsync();
        var item = attachment.Review;
        var service = App.Get<IMcpReviewContextService>();
        var context = (await service.GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldSucceed();
        context.Submission.ExternalUrl.ShouldBe("https://example.test/solution");
        context.Submission.Attachment!.AttachmentId.ShouldBe(attachment.Attachment.AttachmentId);
        var first = (await service.ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, attachment.Attachment.AttachmentId, 0, 65_536, default)).ShouldSucceed();
        first.Content.ShouldBe(attachment.Bytes.Take(65_536).ToArray());
        first.NextOffset.ShouldBe(65_536);
        var last = (await service.ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, attachment.Attachment.AttachmentId, first.NextOffset!.Value, 65_536, default)).ShouldSucceed();
        last.Content.ShouldBe(attachment.Bytes.Skip(65_536).ToArray());
        last.NextOffset.ShouldBeNull();
        var eof = (await service.ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, attachment.Attachment.AttachmentId, attachment.Bytes.Length, 1, default)).ShouldSucceed();
        eof.Content.ShouldBeEmpty();
        eof.NextOffset.ShouldBeNull();
        foreach (var (offset, count) in new (long, int)[] { (-1, 1), (100_001, 1), (0, 0), (0, -1), (0, 65_537) })
            (await service.ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
                item.SubmissionId, attachment.Attachment.AttachmentId, offset, count, default))
                .ShouldFail(ErrorKind.Validation, "mcp_review.invalid_range");
    }

    [Fact]
    public async Task All_MCP_reads_and_writes_require_current_consent_and_do_not_publish()
    {
        var attachment = await PracticalAsync(consent: false);
        var item = attachment.Review;
        await AssertDeniedAsync(App, attachment, ErrorKind.Conflict, "mcp_review.consent_required");
        await GrantAsync(item.Mentee);
        var draft = (await App.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            new(item.SubmissionId, Complete(item), null), default)).ShouldSucceed();
        draft.Version.ShouldBe(1);
        (await App.Get<IMcpReviewDraftService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, default)).ShouldSucceed()!.DraftId.ShouldBe(draft.DraftId);
        (await App.Db(d => d.MentorFeedback.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        var consent = (await App.Get<IMentorshipReviewConsentService>().GetAsync(item.Mentee.UserId, item.Mentee.MentorshipId, default)).ShouldSucceed();
        (await App.Get<IMentorshipReviewConsentService>().RevokeAsync(item.Mentee.UserId,
            new(item.Mentee.MentorshipId, consent.Version!.Value), default)).ShouldSucceed();
        await AssertDeniedAsync(App, attachment, ErrorKind.Conflict, "mcp_review.consent_required");
        (await App.Get<IReviewDraftService>().GetAsync(item.Mentor.UserId, item.SubmissionId, default)).ShouldSucceed()!.DraftId.ShouldBe(draft.DraftId);
    }

    [Fact]
    public async Task Disabled_gate_and_changed_disclosure_deny_existing_consents()
    {
        var attachment = await PracticalAsync();
        await using var disabled = await TestApp.CreateAsync(Database);
        await AssertDeniedAsync(disabled, attachment, ErrorKind.Unavailable, "mcp_review.disabled");
        var settings = EnabledSettings;
        settings["Mcp:Reviews:ProviderNotice"] = "Inny dostawca i nowe zasady.";
        await using var changed = await TestApp.CreateAsync(Database, settings);
        await AssertDeniedAsync(changed, attachment, ErrorKind.Conflict, "mcp_review.consent_required");
    }

    [Fact]
    public async Task Paused_mentorship_allows_consented_review_but_closed_denies_every_MCP_operation()
    {
        var attachment = await PracticalAsync();
        var item = attachment.Review;
        await App.Db(d => d.Mentorships.Where(m => m.Id == item.Mentee.MentorshipId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MentorshipStatus.Paused)));
        (await App.Get<IMcpReviewContextService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, default)).ShouldSucceed();
        await App.Db(d => d.Mentorships.Where(m => m.Id == item.Mentee.MentorshipId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MentorshipStatus.Closed)));
        await AssertDeniedAsync(App, attachment, ErrorKind.Conflict, "mcp_review.mentorship_closed");
    }

    [Fact]
    public async Task MCP_methods_hide_foreign_mentors_mentees_unknown_users_and_unknown_resources()
    {
        var attachment = await PracticalAsync();
        var item = attachment.Review;
        var foreign = await Data.MentorAsync();
        var foreignMentee = await Data.MenteeAsync(foreign);
        foreach (var actor in new[] { foreign.UserId, foreignMentee.UserId, item.Mentee.UserId, Guid.NewGuid() })
        {
            (await App.Get<IMcpReviewContextService>().GetAsync(actor, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldBeNotFound();
            (await App.Get<IMcpReviewContextService>().GetHistoryAsync(actor, item.Mentee.MentorshipId,
                item.Assignment.AssignmentId, null, 20, default)).ShouldBeNotFound();
            (await App.Get<IMcpReviewContextService>().ReadAttachmentAsync(actor, item.Mentee.MentorshipId,
                item.SubmissionId, attachment.Attachment.AttachmentId, 0, 1, default)).ShouldBeNotFound();
            (await App.Get<IMcpReviewDraftService>().GetAsync(actor, item.Mentee.MentorshipId, item.SubmissionId, default)).ShouldBeNotFound();
            (await App.Get<IMcpReviewDraftService>().SaveAsync(actor, item.Mentee.MentorshipId,
                new(item.SubmissionId, Complete(item), null), default)).ShouldBeNotFound();
        }
        var unknown = Guid.NewGuid();
        (await App.Get<IMcpReviewContextService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, unknown, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewContextService>().GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            unknown, null, 20, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, unknown, 0, 1, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewDraftService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId, unknown, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            new(unknown, Complete(item), null), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Same_workspace_different_mentorship_submission_assignment_and_attachment_cannot_be_mixed()
    {
        var attachment = await PracticalAsync();
        var item = attachment.Review;
        var other = await Data.MenteeAsync(item.Mentor);
        await GrantAsync(other);
        (await App.Get<IMcpReviewContextService>().GetAsync(item.Mentor.UserId, other.MentorshipId, item.SubmissionId, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewContextService>().GetHistoryAsync(item.Mentor.UserId, other.MentorshipId,
            item.Assignment.AssignmentId, null, 20, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId, other.MentorshipId,
            item.SubmissionId, attachment.Attachment.AttachmentId, 0, 1, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewDraftService>().GetAsync(item.Mentor.UserId, other.MentorshipId, item.SubmissionId, default)).ShouldBeNotFound();
        (await App.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, other.MentorshipId,
            new(item.SubmissionId, Complete(item), null), default)).ShouldBeNotFound();
        var secondAssignment = await Data.PublishedAssignmentAsync(item.Mentor, other, ExerciseType.ArchitecturalDecision);
        var secondSubmission = await Data.DecisionSubmissionAsync(other, secondAssignment.AssignmentId);
        (await App.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId, other.MentorshipId,
            secondSubmission.SubmissionId, attachment.Attachment.AttachmentId, 0, 1, default)).ShouldBeNotFound();
    }

    private static async Task AssertDeniedAsync(TestApp app, AttachmentCase attachment, ErrorKind kind, string code)
    {
        var item = attachment.Review;
        (await app.Get<IMcpReviewContextService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, default)).ShouldFail(kind, code);
        (await app.Get<IMcpReviewContextService>().GetHistoryAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.Assignment.AssignmentId, null, 20, default)).ShouldFail(kind, code);
        (await app.Get<IMcpReviewContextService>().ReadAttachmentAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, attachment.Attachment.AttachmentId, 0, 1, default)).ShouldFail(kind, code);
        (await app.Get<IMcpReviewDraftService>().GetAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            item.SubmissionId, default)).ShouldFail(kind, code);
        (await app.Get<IMcpReviewDraftService>().SaveAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            new(item.SubmissionId, Complete(item), null), default)).ShouldFail(kind, code);
    }
}
