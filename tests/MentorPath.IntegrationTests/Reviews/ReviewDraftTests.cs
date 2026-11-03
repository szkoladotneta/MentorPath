using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Reviews;

public sealed class ReviewDraftTests(PostgresFixture db) : ReviewFeatureTestBase(db)
{
    [Fact]
    public async Task Closed_mentorship_preserves_readable_draft_but_denies_human_draft_writes()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        (await App.Get<IMentorshipService>().ChangeStatusAsync(item.Mentor.UserId, item.Mentee.MentorshipId,
            MentorshipStatus.Closed, default)).ShouldSucceed();
        var human = App.Get<IReviewDraftService>();
        (await human.GetAsync(item.Mentor.UserId, item.SubmissionId, default)).ShouldSucceed()!.Version.ShouldBe(draft.Version);
        (await human.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), draft.Version), default))
            .ShouldFail(ErrorKind.Conflict, "review.mentorship_closed");
        (await human.PublishAsync(item.Mentor.UserId, item.SubmissionId, draft.Version, default))
            .ShouldFail(ErrorKind.Conflict, "review.mentorship_closed");
        (await App.Db(d => d.ReviewDrafts.SingleAsync(x => x.Id == draft.DraftId))).Version.ShouldBe(draft.Version);
        (await App.Db(d => d.MentorFeedback.AnyAsync(x => x.SubmissionId == item.SubmissionId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Changed_review_and_closure_methods_hide_unknown_actors()
    {
        var item = await PendingAsync();
        var content = Complete(item);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(Guid.NewGuid(), new(item.SubmissionId,
            content.StrengthsMarkdown!, content.ImprovementsMarkdown!, content.ChallengeQuestionMarkdown!,
            content.Verdict!.Value, content.Scores), default)).ShouldBeNotFound();
        (await App.Get<IMentorshipService>().ChangeStatusAsync(Guid.NewGuid(), item.Mentee.MentorshipId,
            MentorshipStatus.Closed, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Partial_create_and_versioned_update_replace_scores_without_publishing()
    {
        var item = await PendingAsync();
        (await App.Get<IReviewDraftService>().GetAsync(item.Mentor.UserId, item.SubmissionId, default)).ShouldSucceed().ShouldBeNull();
        var first = await SaveAsync(item, new(null, null, null, null, []));
        first.Version.ShouldBe(1);
        first.Content.Verdict.ShouldBeNull();
        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await SaveAsync(item, Complete(item), first.Version);
        second.DraftId.ShouldBe(first.DraftId);
        second.Version.ShouldBe(2);
        second.CreatedAtUtc.ShouldBe(first.CreatedAtUtc);
        second.UpdatedAtUtc.ShouldBe(first.CreatedAtUtc.AddMinutes(1));
        second.Content.StrengthsMarkdown.ShouldBe("Mocne strony");
        second.Content.Scores.ShouldAllBe(s => s.CommentMarkdown == "Komentarz");
        var third = await SaveAsync(item, new("Nowe", null, null, null, []), second.Version);
        third.Version.ShouldBe(3);
        third.Content.Scores.ShouldBeEmpty();
        (await App.Db(d => d.ReviewDraftCriterionScores.CountAsync(s => s.DraftId == first.DraftId))).ShouldBe(0);
        (await App.Db(d => d.MentorFeedback.CountAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBe(0);
        (await App.Db(d => d.Submissions.SingleAsync(s => s.Id == item.SubmissionId))).Status.ShouldBe(SubmissionStatus.PendingReview);
        (await App.Db(d => d.ActivityEvents.CountAsync(e => e.SubmissionId == item.SubmissionId &&
            e.EventType == ActivityEventType.ReviewSubmitted))).ShouldBe(0);
    }

    [Fact]
    public async Task Explicit_stale_versions_do_not_fall_back_to_create_or_overwrite()
    {
        var item = await PendingAsync();
        var service = App.Get<IReviewDraftService>();
        (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), 1), default))
            .ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        var first = await SaveAsync(item);
        foreach (var version in new long?[] { null, 0, -1, 2 })
            (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), version), default))
                .ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        await SaveAsync(item, Complete(item) with { StrengthsMarkdown = "Zmienione" }, first.Version);
        (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), first.Version), default))
            .ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        (await service.PublishAsync(item.Mentor.UserId, item.SubmissionId, first.Version, default))
            .ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        (await service.GetAsync(item.Mentor.UserId, item.SubmissionId, default)).ShouldSucceed()!.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Publish_requires_complete_current_feedback_and_atomically_consumes_the_draft()
    {
        var item = await PendingAsync();
        var partial = await SaveAsync(item, new(null, null, null, ReviewVerdict.Accepted, []));
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, partial.Version, default))
            .ShouldFail(ErrorKind.Validation, "review.invalid");
        (await App.Db(d => d.MentorFeedback.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        (await App.Db(d => d.Submissions.SingleAsync(s => s.Id == item.SubmissionId))).Status.ShouldBe(SubmissionStatus.PendingReview);
        var complete = await SaveAsync(item, Complete(item), partial.Version);
        var id = (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, complete.Version, default)).ShouldSucceed();
        var feedback = await App.Db(d => d.MentorFeedback.Include(f => f.CriterionScores).SingleAsync(f => f.Id == id));
        feedback.StrengthsMarkdown.ShouldBe("Mocne strony");
        feedback.CriterionScores.Count.ShouldBe(2);
        (await App.Db(d => d.Submissions.SingleAsync(s => s.Id == item.SubmissionId))).Status.ShouldBe(SubmissionStatus.Accepted);
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(s => s.DraftId == complete.DraftId))).ShouldBeFalse();
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, complete.Version, default))
            .ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
    }

    [Fact]
    public async Task Direct_human_feedback_consumes_a_draft_and_new_saves_are_not_pending()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        var content = Complete(item, ReviewVerdict.NeedsRevision);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(item.Mentor.UserId, new(item.SubmissionId,
            content.StrengthsMarkdown!, content.ImprovementsMarkdown!, content.ChallengeQuestionMarkdown!, content.Verdict!.Value, content.Scores),
            default)).ShouldSucceed();
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(s => s.DraftId == draft.DraftId))).ShouldBeFalse();
        (await App.Get<IReviewDraftService>().SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), null), default))
            .ShouldFail(ErrorKind.Conflict, "review.not_pending");
    }

    [Fact]
    public async Task Publication_revalidates_current_active_selected_rubric()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        (await App.Get<IRubricService>().SetActiveAsync(item.Mentor.UserId, item.Criteria[0].CriterionId, false, default)).ShouldSucceed();
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, draft.Version, default))
            .ShouldFail(ErrorKind.Validation, "review.invalid");
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.Id == draft.DraftId))).ShouldBeTrue();
        var corrected = await SaveAsync(item, Complete(item) with { Scores = [new(item.Criteria[1].CriterionId, 5, "")] }, draft.Version);
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, corrected.Version, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Failed_publication_rolls_back_status_transition_and_preserves_draft_scores()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        await App.Db(async d =>
        {
            d.ActivityEvents.Add(new ActivityEvent
            {
                Id = Guid.NewGuid(), WorkspaceId = item.Mentor.WorkspaceId, AssignmentId = item.Assignment.AssignmentId,
                SubmissionId = item.SubmissionId, MenteeUserId = item.Mentee.UserId, MentorUserId = item.Mentor.UserId,
                EventType = ActivityEventType.ReviewOpened, OccurredAtUtc = App.Clock.GetUtcNow().UtcDateTime.AddHours(1),
            });
            return await d.SaveChangesAsync();
        });
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, item.SubmissionId, draft.Version, default))
            .ShouldFail(ErrorKind.Conflict, "review.clock_error");
        (await App.Db(d => d.Submissions.SingleAsync(s => s.Id == item.SubmissionId))).Status.ShouldBe(SubmissionStatus.PendingReview);
        (await App.Db(d => d.MentorFeedback.AnyAsync(s => s.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDrafts.SingleAsync(s => s.Id == draft.DraftId))).Version.ShouldBe(draft.Version);
        (await App.Db(d => d.ReviewDraftCriterionScores.CountAsync(s => s.DraftId == draft.DraftId))).ShouldBe(2);
    }

    [Fact]
    public async Task Draft_scores_reject_foreign_unselected_inactive_duplicate_and_invalid_values()
    {
        var item = await PendingAsync();
        var foreign = await Data.MentorAsync();
        var foreignCriteria = (await App.Get<IRubricService>().ListCriteriaAsync(foreign.UserId, true, default)).ShouldSucceed();
        var service = App.Get<IReviewDraftService>();
        (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId,
            Complete(item) with { Scores = [new(foreignCriteria[0].CriterionId, 4, "")] }, null), default)).ShouldBeNotFound();
        foreach (var scores in new IReadOnlyList<CriterionScoreInput>[]
        {
            [new(item.Criteria[2].CriterionId, 4, "")],
            [new(item.Criteria[0].CriterionId, 4, ""), new(item.Criteria[0].CriterionId, 4, "")],
            [new(item.Criteria[0].CriterionId, 0, "")],
            [new(item.Criteria[0].CriterionId, 6, "")],
            [new(item.Criteria[0].CriterionId, 3, new string('x', InputLimits.Markdown + 1))],
        })
            (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item) with { Scores = scores }, null), default))
                .ShouldFail(ErrorKind.Validation, "review_draft.invalid");
        (await App.Get<IRubricService>().SetActiveAsync(item.Mentor.UserId, item.Criteria[0].CriterionId, false, default)).ShouldSucceed();
        (await service.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), null), default))
            .ShouldFail(ErrorKind.Validation, "review_draft.invalid");
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Human_drafts_survive_revocation_and_are_independent_of_the_MCP_gate()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        var consent = (await App.Get<IMentorshipReviewConsentService>().GetAsync(item.Mentee.UserId, item.Mentee.MentorshipId, default)).ShouldSucceed();
        (await App.Get<IMentorshipReviewConsentService>().RevokeAsync(item.Mentee.UserId,
            new(item.Mentee.MentorshipId, consent.Version!.Value), default)).ShouldSucceed();
        await using var disabled = await TestApp.CreateAsync(Database);
        var human = disabled.Get<IReviewDraftService>();
        (await human.GetAsync(item.Mentor.UserId, item.SubmissionId, default)).ShouldSucceed()!.DraftId.ShouldBe(draft.DraftId);
        var updated = (await human.SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), draft.Version), default)).ShouldSucceed();
        (await human.PublishAsync(item.Mentor.UserId, item.SubmissionId, updated.Version, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Human_draft_methods_hide_foreign_missing_and_own_mentee_resources()
    {
        var item = await PendingAsync();
        var foreign = await Data.MentorAsync();
        var foreignMentee = await Data.MenteeAsync(foreign);
        var draft = await SaveAsync(item);
        foreach (var actor in new[] { foreign.UserId, foreignMentee.UserId, item.Mentee.UserId, Guid.NewGuid() })
        {
            (await App.Get<IReviewDraftService>().GetAsync(actor, item.SubmissionId, default)).ShouldBeNotFound();
            (await App.Get<IReviewDraftService>().SaveAsync(actor, new(item.SubmissionId, Complete(item), draft.Version), default)).ShouldBeNotFound();
            (await App.Get<IReviewDraftService>().PublishAsync(actor, item.SubmissionId, draft.Version, default)).ShouldBeNotFound();
        }
        var missing = Guid.NewGuid();
        (await App.Get<IReviewDraftService>().GetAsync(item.Mentor.UserId, missing, default)).ShouldBeNotFound();
        (await App.Get<IReviewDraftService>().SaveAsync(item.Mentor.UserId, new(missing, Complete(item), null), default)).ShouldBeNotFound();
        (await App.Get<IReviewDraftService>().PublishAsync(item.Mentor.UserId, missing, 1, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Concurrent_creates_and_updates_have_one_winner_without_last_writer_fallback()
    {
        var item = await PendingAsync();
        var created = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => App.Get<IReviewDraftService>()
            .SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), null), default)));
        created.Count(r => r.IsSuccess).ShouldBe(1);
        created.Single(r => !r.IsSuccess).ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        var draft = created.Single(r => r.IsSuccess).Value!;
        var updated = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => App.Get<IReviewDraftService>()
            .SaveAsync(item.Mentor.UserId, new(item.SubmissionId, Complete(item), draft.Version), default)));
        updated.Count(r => r.IsSuccess).ShouldBe(1);
        updated.Single(r => !r.IsSuccess).ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        (await App.Db(d => d.ReviewDrafts.SingleAsync(f => f.Id == draft.DraftId))).Version.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_draft_publication_is_once_and_leaves_no_draft_or_scores()
    {
        var item = await PendingAsync();
        var draft = await SaveAsync(item);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => App.Get<IReviewDraftService>()
            .PublishAsync(item.Mentor.UserId, item.SubmissionId, draft.Version, default)));
        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => !r.IsSuccess).ShouldFail(ErrorKind.Conflict, "review_draft.version_conflict");
        (await App.Db(d => d.MentorFeedback.CountAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBe(1);
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.Id == draft.DraftId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(s => s.DraftId == draft.DraftId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Create_racing_direct_publication_cannot_leave_a_draft_on_reviewed_submission()
    {
        var item = await PendingAsync();
        var content = Complete(item);
        var saving = App.Get<IReviewDraftService>().SaveAsync(item.Mentor.UserId, new(item.SubmissionId, content, null), default);
        var publishing = App.Get<IReviewService>().SubmitFeedbackAsync(item.Mentor.UserId,
            new(item.SubmissionId, content.StrengthsMarkdown!, content.ImprovementsMarkdown!,
                content.ChallengeQuestionMarkdown!, content.Verdict!.Value, content.Scores), default);
        await Task.WhenAll(saving, publishing);
        (await publishing).ShouldSucceed();
        var saved = await saving;
        if (!saved.IsSuccess) saved.ShouldFail(ErrorKind.Conflict, "review.not_pending");
        (await App.Db(d => d.ReviewDrafts.AnyAsync(f => f.SubmissionId == item.SubmissionId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(s => s.Draft!.SubmissionId == item.SubmissionId))).ShouldBeFalse();
    }
}
