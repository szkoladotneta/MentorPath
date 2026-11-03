using System.Text.Json;
using MentorPath.Application.Common;
using MentorPath.Application.Privacy.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.IntegrationTests.Privacy;

public sealed class ReviewDataPrivacyTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Human_draft_save_waits_for_erasure_and_cannot_recreate_erased_content()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var app = await TestApp.CreateAsync(Database, overrides: services =>
            services.AddSingleton<IUserValidator<AppUser>>(new BlockingAnonymizationValidator(entered, release)));
        var mentor = await app.Data.MentorAsync();
        var subject = await app.Data.MenteeAsync(mentor);
        var seeded = await SeedAsync(app, mentor, subject);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var erasing = app.Get<IPersonalDataService>().AnonymizeAsync(new(subject.UserId, null), true, timeout.Token);
        await entered.Task.WaitAsync(timeout.Token);
        var saving = app.Get<IReviewDraftService>().SaveAsync(mentor.UserId,
            new(seeded.SubmissionId, new("Concurrent private text", null, null, null, []), null), timeout.Token);
        try { saving.IsCompleted.ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        (await erasing).ShouldSucceed();
        (await saving).ShouldFail(ErrorKind.Conflict, "review.mentorship_closed");
        (await app.Db(d => d.ReviewDrafts.AnyAsync(x => x.Id == seeded.DraftId || x.SubmissionId == seeded.SubmissionId))).ShouldBeFalse();
        (await app.Db(d => d.ReviewDraftCriterionScores.AnyAsync(x => x.DraftId == seeded.DraftId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Export_includes_active_and_revoked_consent_partial_and_complete_drafts_and_only_subject_records()
    {
        var mentor = await Data.MentorAsync();
        var subject = await Data.MenteeAsync(mentor);
        var neighbor = await Data.MenteeAsync(mentor);
        var otherMentor = await Data.MentorAsync();
        var otherMentee = await Data.MenteeAsync(otherMentor);
        var secondMentorship = await Data.MenteeAsync(otherMentor, subject.Email, subject.UserId);
        var partial = await SeedAsync(App, mentor, subject);
        var complete = await SeedAsync(App, otherMentor, secondMentorship);
        var sameWorkspace = await SeedAsync(App, mentor, neighbor);
        var foreign = await SeedAsync(App, otherMentor, otherMentee);
        var revokedAt = App.Clock.GetUtcNow().UtcDateTime;
        await App.Db(async d =>
        {
            var consent = await d.MentorshipReviewConsents.SingleAsync(c => c.Id == complete.ConsentId);
            consent.RevokedAtUtc = revokedAt;
            consent.Version = 2;
            var draft = await d.ReviewDrafts.SingleAsync(x => x.Id == complete.DraftId);
            draft.Verdict = ReviewVerdict.NeedsRevision;
            draft.ImprovementsMarkdown = "Improve subject answer";
            draft.ChallengeQuestionMarkdown = "Subject challenge";
            draft.Version = 3;
            return await d.SaveChangesAsync();
        });

        var export = (await App.Get<IPersonalDataService>().ExportAsync(new(subject.UserId, null), default)).ShouldSucceed();

        export.SchemaVersion.ShouldBe(3);
        var consents = export.ReviewConsents.ShouldNotBeNull();
        consents.Select(c => c.ConsentId).Order().ShouldBe(new[] { partial.ConsentId, complete.ConsentId }.Order());
        var active = consents.Single(c => c.ConsentId == partial.ConsentId);
        active.MentorshipId.ShouldBe(subject.MentorshipId);
        active.RevokedAtUtc.ShouldBeNull();
        active.Version.ShouldBe(1);
        active.DisclosureVersion.ShouldBe(new McpReviewOptions().DisclosureVersion);
        active.GrantedAtUtc.ShouldBe(revokedAt);
        var revoked = consents.Single(c => c.ConsentId == complete.ConsentId);
        revoked.MentorshipId.ShouldBe(secondMentorship.MentorshipId);
        revoked.RevokedAtUtc.ShouldBe(revokedAt);
        revoked.Version.ShouldBe(2);
        var drafts = export.ReviewDrafts.ShouldNotBeNull();
        drafts.Count.ShouldBe(2);
        var partialExport = drafts.Single(d => d.DraftId == partial.DraftId);
        partialExport.SubmissionId.ShouldBe(partial.SubmissionId);
        partialExport.MentorshipId.ShouldBe(subject.MentorshipId);
        partialExport.Verdict.ShouldBeNull();
        partialExport.StrengthsMarkdown.ShouldBe("Subject draft strength");
        partialExport.ImprovementsMarkdown.ShouldBeNull();
        partialExport.ChallengeQuestionMarkdown.ShouldBeNull();
        partialExport.CreatedAtUtc.ShouldBe(revokedAt);
        partialExport.UpdatedAtUtc.ShouldBe(revokedAt);
        partialExport.Version.ShouldBe(1);
        var score = partialExport.CriterionScores.ShouldHaveSingleItem();
        var criterion = await App.Db(d => d.RubricCriteria.SingleAsync(c => c.Id == score.CriterionId));
        score.CriterionName.ShouldBe(criterion.Name);
        score.Position.ShouldBe(criterion.Position);
        score.Score.ShouldBe(3);
        score.CommentMarkdown.ShouldBe("Subject draft score");
        var completeExport = drafts.Single(d => d.DraftId == complete.DraftId);
        completeExport.Verdict.ShouldBe(ReviewVerdict.NeedsRevision);
        completeExport.ImprovementsMarkdown.ShouldBe("Improve subject answer");
        completeExport.ChallengeQuestionMarkdown.ShouldBe("Subject challenge");
        completeExport.Version.ShouldBe(3);
        completeExport.CriterionScores.Count.ShouldBe(1);
        var json = JsonSerializer.Serialize(export);
        foreach (var excluded in new[] { sameWorkspace, foreign })
        {
            json.ShouldNotContain(excluded.DraftId.ToString());
            json.ShouldNotContain(excluded.ConsentId.ToString());
        }
        await AssertPresentAsync(partial);
    }

    [Fact]
    public async Task Export_populates_empty_collections_instead_of_legacy_null()
    {
        var mentor = await Data.MentorAsync();
        var subject = await Data.MenteeAsync(mentor);
        var export = (await App.Get<IPersonalDataService>().ExportAsync(new(subject.UserId, null), default)).ShouldSucceed();
        export.ReviewConsents.ShouldNotBeNull().ShouldBeEmpty();
        export.ReviewDrafts.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public async Task Repeat_anonymization_removes_review_data_created_after_the_first_run()
    {
        var mentor = await Data.MentorAsync();
        var subject = await Data.MenteeAsync(mentor);
        var seeded = await SeedAsync(App, mentor, subject);
        var privacy = App.Get<IPersonalDataService>();
        (await privacy.AnonymizeAsync(new(subject.UserId, null), true, default)).ShouldSucceed();
        await App.Db(async d =>
        {
            var now = App.Clock.GetUtcNow().UtcDateTime;
            d.MentorshipReviewConsents.Add(new MentorshipReviewConsent
            {
                Id = seeded.ConsentId, WorkspaceId = mentor.WorkspaceId, MentorshipId = subject.MentorshipId,
                DisclosureVersion = "restored-evidence", GrantedAtUtc = now, Version = 1
            });
            d.ReviewDrafts.Add(new ReviewDraft
            {
                Id = seeded.DraftId, WorkspaceId = mentor.WorkspaceId, SubmissionId = seeded.SubmissionId,
                StrengthsMarkdown = "Restored draft", CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1,
                CriterionScores =
                [
                    new ReviewDraftCriterionScore
                    {
                        WorkspaceId = mentor.WorkspaceId, DraftId = seeded.DraftId,
                        CriterionId = (await d.RubricCriteria.FirstAsync(c => c.WorkspaceId == mentor.WorkspaceId)).Id,
                        Score = 2, CommentMarkdown = "Restored score"
                    }
                ]
            });
            return await d.SaveChangesAsync();
        });
        (await privacy.AnonymizeAsync(new(subject.UserId, null), true, default)).ShouldSucceed().AlreadyAnonymized.ShouldBeTrue();
        (await App.Db(d => d.ReviewDrafts.AnyAsync(x => x.Id == seeded.DraftId))).ShouldBeFalse();
        (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(x => x.DraftId == seeded.DraftId))).ShouldBeFalse();
        (await App.Db(d => d.MentorshipReviewConsents.AnyAsync(x => x.Id == seeded.ConsentId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Dry_run_preserves_drafts_scores_and_consent_and_apply_removes_only_the_subject_records()
    {
        var mentor = await Data.MentorAsync();
        var subject = await Data.MenteeAsync(mentor);
        var neighbor = await Data.MenteeAsync(mentor);
        var foreignMentor = await Data.MentorAsync();
        var foreignMentee = await Data.MenteeAsync(foreignMentor);
        var secondMentorship = await Data.MenteeAsync(foreignMentor, subject.Email, subject.UserId);
        var own = await SeedAsync(App, mentor, subject);
        var secondOwn = await SeedAsync(App, foreignMentor, secondMentorship);
        var sameWorkspace = await SeedAsync(App, mentor, neighbor);
        var foreign = await SeedAsync(App, foreignMentor, foreignMentee);
        var privacy = App.Get<IPersonalDataService>();

        (await privacy.AnonymizeAsync(new(subject.UserId, null), false, default)).ShouldSucceed().Applied.ShouldBeFalse();
        await AssertPresentAsync(own);
        await AssertPresentAsync(secondOwn);

        (await privacy.AnonymizeAsync(new(subject.UserId, null), true, default)).ShouldSucceed().Applied.ShouldBeTrue();
        foreach (var deleted in new[] { own, secondOwn })
        {
            (await App.Db(d => d.ReviewDraftCriterionScores.AnyAsync(s => s.DraftId == deleted.DraftId))).ShouldBeFalse();
            (await App.Db(d => d.ReviewDrafts.AnyAsync(s => s.Id == deleted.DraftId))).ShouldBeFalse();
            (await App.Db(d => d.MentorshipReviewConsents.AnyAsync(s => s.Id == deleted.ConsentId))).ShouldBeFalse();
        }
        await AssertPresentAsync(sameWorkspace);
        await AssertPresentAsync(foreign);
        (await privacy.AnonymizeAsync(new(subject.UserId, null), true, default)).ShouldSucceed().AlreadyAnonymized.ShouldBeTrue();
        await AssertPresentAsync(sameWorkspace);
        await AssertPresentAsync(foreign);
    }

    [Fact]
    public async Task Identity_failure_rolls_back_draft_score_and_consent_removal_in_the_existing_transaction()
    {
        await using var app = await TestApp.CreateAsync(Database, overrides: services =>
            services.AddSingleton<IUserValidator<AppUser>>(new RejectAnonymizationValidator()));
        var mentor = await app.Data.MentorAsync();
        var mentee = await app.Data.MenteeAsync(mentor);
        var seeded = await SeedAsync(app, mentor, mentee);

        (await app.Get<IPersonalDataService>().AnonymizeAsync(new(mentee.UserId, null), true, default))
            .ShouldFail(ErrorKind.Conflict, "privacy.account_update_failed");
        (await app.Db(d => d.ReviewDrafts.SingleAsync(x => x.Id == seeded.DraftId))).StrengthsMarkdown.ShouldBe("Subject draft strength");
        (await app.Db(d => d.ReviewDraftCriterionScores.SingleAsync(x => x.DraftId == seeded.DraftId))).CommentMarkdown.ShouldBe("Subject draft score");
        (await app.Db(d => d.MentorshipReviewConsents.SingleAsync(x => x.Id == seeded.ConsentId))).RevokedAtUtc.ShouldBeNull();
        (await app.Db(d => d.Mentorships.SingleAsync(x => x.Id == mentee.MentorshipId))).Status.ShouldBe(MentorshipStatus.Active);
    }

    [Fact]
    public async Task Ordinary_mentee_feedback_does_not_expose_unpublished_draft_content()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var seeded = await SeedAsync(App, mentor, mentee);
        var assignment = (await App.Get<ISubmissionService>()
            .GetAssignmentForMenteeAsync(mentee.UserId, seeded.AssignmentId, default)).ShouldSucceed();
        var serialized = JsonSerializer.Serialize(assignment);
        serialized.ShouldNotContain("Subject draft strength");
        serialized.ShouldNotContain("Subject draft score");
    }

    private async Task AssertPresentAsync(SeededRecords seeded)
    {
        (await App.Db(d => d.ReviewDrafts.SingleAsync(x => x.Id == seeded.DraftId))).StrengthsMarkdown.ShouldBe("Subject draft strength");
        (await App.Db(d => d.ReviewDraftCriterionScores.SingleAsync(x => x.DraftId == seeded.DraftId))).CommentMarkdown.ShouldBe("Subject draft score");
        (await App.Db(d => d.MentorshipReviewConsents.SingleAsync(x => x.Id == seeded.ConsentId))).Version.ShouldBe(1);
    }

    private sealed record SeededRecords(Guid DraftId, Guid ConsentId, Guid SubmissionId, Guid AssignmentId);

    private static async Task<SeededRecords> SeedAsync(TestApp app, MentorRef mentor, MenteeRef mentee)
    {
        var assignment = await app.Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await app.Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var criterionId = (await app.Data.ScoresAsync(mentor)).First().CriterionId;
        var draftId = Guid.NewGuid();
        var consentId = Guid.NewGuid();
        var now = app.Clock.GetUtcNow().UtcDateTime;
        await app.Db(async d =>
        {
            d.MentorshipReviewConsents.Add(new MentorshipReviewConsent
            {
                Id = consentId, WorkspaceId = mentor.WorkspaceId, MentorshipId = mentee.MentorshipId,
                DisclosureVersion = new McpReviewOptions().DisclosureVersion, GrantedAtUtc = now, Version = 1
            });
            d.ReviewDrafts.Add(new ReviewDraft
            {
                Id = draftId, WorkspaceId = mentor.WorkspaceId, SubmissionId = submission.SubmissionId,
                StrengthsMarkdown = "Subject draft strength", CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1,
                CriterionScores =
                [
                    new ReviewDraftCriterionScore
                    {
                        WorkspaceId = mentor.WorkspaceId, DraftId = draftId, CriterionId = criterionId,
                        Score = 3, CommentMarkdown = "Subject draft score"
                    }
                ]
            });
            return await d.SaveChangesAsync();
        });
        return new(draftId, consentId, submission.SubmissionId, assignment.AssignmentId);
    }

    private sealed class RejectAnonymizationValidator : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user) =>
            Task.FromResult(user.Email?.EndsWith("@anonymized.invalid", StringComparison.Ordinal) == true
                ? IdentityResult.Failed(new IdentityError { Code = "test.anonymization_rejected", Description = "Test rejection." })
                : IdentityResult.Success);
    }

    private sealed class BlockingAnonymizationValidator(TaskCompletionSource entered, TaskCompletionSource release) : IUserValidator<AppUser>
    {
        public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
        {
            if (user.Email?.EndsWith("@anonymized.invalid", StringComparison.Ordinal) == true)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            return IdentityResult.Success;
        }
    }
}
