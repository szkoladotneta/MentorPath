using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MentorPath.IntegrationTests.Database;

public sealed class ReviewPreparationSchemaTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Schema_contracts_are_not_registered_as_unfinished_services()
    {
        await using var scope = App.CreateScope();
        scope.ServiceProvider.GetService<IMentorshipReviewConsentService>().ShouldBeNull();
        scope.ServiceProvider.GetService<IMcpReviewContextService>().ShouldBeNull();
        scope.ServiceProvider.GetService<IMcpReviewDraftService>().ShouldBeNull();
        scope.ServiceProvider.GetService<IReviewDraftService>().ShouldBeNull();
        scope.ServiceProvider.GetRequiredService<IReviewService>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Existing_mentorship_has_no_consent_and_grant_revocation_round_trips()
    {
        var consent = await NewConsentAsync();
        (await App.Db(ctx => ctx.MentorshipReviewConsents.AnyAsync(c => c.MentorshipId == consent.MentorshipId))).ShouldBeFalse();
        await InsertConsentAsync(consent);

        var saved = await App.Db(ctx => ctx.MentorshipReviewConsents.AsNoTracking().SingleAsync(c => c.Id == consent.Id));
        saved.WorkspaceId.ShouldBe(consent.WorkspaceId);
        saved.MentorshipId.ShouldBe(consent.MentorshipId);
        saved.DisclosureVersion.ShouldBe(consent.DisclosureVersion);
        saved.GrantedAtUtc.ShouldBe(consent.GrantedAtUtc);
        saved.RevokedAtUtc.ShouldBeNull();
        saved.Version.ShouldBe(1);

        await App.Db(async ctx =>
        {
            var row = await ctx.MentorshipReviewConsents.SingleAsync(c => c.Id == consent.Id);
            row.RevokedAtUtc = row.GrantedAtUtc.AddMinutes(1);
            row.Version++;
            return await ctx.SaveChangesAsync();
        });
        saved = await App.Db(ctx => ctx.MentorshipReviewConsents.AsNoTracking().SingleAsync(c => c.Id == consent.Id));
        saved.RevokedAtUtc.ShouldBe(consent.GrantedAtUtc.AddMinutes(1));
        saved.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Consent_does_not_apply_to_the_same_mentee_in_another_workspace()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var menteeA = await Data.MenteeAsync(mentorA);
        var menteeB = await Data.MenteeAsync(mentorB, menteeA.Email, menteeA.UserId);
        await InsertConsentAsync(NewConsent(mentorA.WorkspaceId, menteeA.MentorshipId));

        (await App.Db(ctx => ctx.MentorshipReviewConsents.AnyAsync(c => c.MentorshipId == menteeB.MentorshipId))).ShouldBeFalse();
    }

    [Fact]
    public async Task Consent_rejects_duplicate_mentorship()
    {
        var consent = await NewConsentAsync();
        await InsertConsentAsync(consent);
        consent.Id = Guid.CreateVersion7();

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertConsentAsync(consent));
        AssertConstraint(error, PostgresErrorCodes.UniqueViolation, "IX_MentorshipReviewConsents_MentorshipId");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consent_rejects_missing_or_foreign_workspace_mentorship(bool foreign)
    {
        var consent = await NewConsentAsync();
        if (foreign) consent.WorkspaceId = (await Data.MentorAsync()).WorkspaceId;
        else consent.MentorshipId = Guid.NewGuid();

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertConsentAsync(consent));
        AssertConstraint(error, PostgresErrorCodes.ForeignKeyViolation,
            "FK_MentorshipReviewConsents_Mentorships_WorkspaceId_Mentorship~");
    }

    [Theory]
    [InlineData("disclosure", "CK_MentorshipReviewConsents_DisclosureVersion")]
    [InlineData("version", "CK_MentorshipReviewConsents_Version")]
    [InlineData("revocation", "CK_MentorshipReviewConsents_RevokedAtUtc")]
    public async Task Consent_rejects_invalid_evidence(string field, string constraint)
    {
        var consent = await NewConsentAsync();
        switch (field)
        {
            case "disclosure": consent.DisclosureVersion = "   "; break;
            case "version": consent.Version = 0; break;
            case "revocation": consent.RevokedAtUtc = consent.GrantedAtUtc.AddTicks(-10); break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertConsentAsync(consent));
        AssertConstraint(error, PostgresErrorCodes.CheckViolation, constraint);
    }

    [Fact]
    public async Task Consent_rejects_disclosure_version_over_limit()
    {
        var consent = await NewConsentAsync();
        consent.DisclosureVersion = new string('v', 101);
        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertConsentAsync(consent));
        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task Consent_concurrency_token_rejects_stale_update()
    {
        var consent = await NewConsentAsync();
        await InsertConsentAsync(consent);

        await App.Db(async stale =>
        {
            var row = await stale.MentorshipReviewConsents.SingleAsync(c => c.Id == consent.Id);
            await App.Db(async current =>
            {
                var latest = await current.MentorshipReviewConsents.SingleAsync(c => c.Id == consent.Id);
                latest.RevokedAtUtc = latest.GrantedAtUtc;
                latest.Version++;
                return await current.SaveChangesAsync();
            });
            row.DisclosureVersion = "stale";
            row.Version++;
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            return 0;
        });
        var saved = await App.Db(ctx => ctx.MentorshipReviewConsents.AsNoTracking().SingleAsync(c => c.Id == consent.Id));
        saved.DisclosureVersion.ShouldBe(consent.DisclosureVersion);
        saved.RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Partial_draft_round_trips_without_publishing_or_changing_history()
    {
        var draft = await NewDraftAsync();
        var submission = await App.Db(ctx => ctx.Submissions.AsNoTracking().SingleAsync(s => s.Id == draft.SubmissionId));
        var eventsBefore = await App.Db(ctx => ctx.ActivityEvents.CountAsync(e => e.SubmissionId == draft.SubmissionId));
        await InsertDraftAsync(draft);

        var saved = await App.Db(ctx => ctx.ReviewDrafts.AsNoTracking().Include(d => d.CriterionScores).SingleAsync(d => d.Id == draft.Id));
        saved.Verdict.ShouldBeNull();
        saved.StrengthsMarkdown.ShouldBeNull();
        saved.ImprovementsMarkdown.ShouldBeNull();
        saved.ChallengeQuestionMarkdown.ShouldBeNull();
        saved.CriterionScores.ShouldBeEmpty();
        saved.Version.ShouldBe(1);
        saved.CreatedAtUtc.ShouldBe(draft.CreatedAtUtc);
        saved.UpdatedAtUtc.ShouldBe(draft.UpdatedAtUtc);
        (await App.Db(ctx => ctx.Submissions.Where(s => s.Id == draft.SubmissionId).Select(s => s.Status).SingleAsync())).ShouldBe(SubmissionStatus.PendingReview);
        (await App.Db(ctx => ctx.MentorFeedback.AnyAsync(f => f.SubmissionId == draft.SubmissionId))).ShouldBeFalse();
        (await App.Db(ctx => ctx.ActivityEvents.CountAsync(e => e.SubmissionId == draft.SubmissionId))).ShouldBe(eventsBefore);
        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(submission.MenteeUserId, submission.AssignmentId, default)).ShouldSucceed();
        history.Attempts.Single(s => s.SubmissionId == draft.SubmissionId).Feedback.ShouldBeNull();
    }

    [Theory]
    [InlineData(ReviewVerdict.Accepted, 1)]
    [InlineData(ReviewVerdict.NeedsRevision, 5)]
    public async Task Draft_and_scores_round_trip_at_field_limits(ReviewVerdict verdict, short score)
    {
        var draft = await NewDraftAsync();
        draft.Verdict = verdict;
        draft.StrengthsMarkdown = new string('s', InputLimits.Markdown);
        draft.ImprovementsMarkdown = new string('i', InputLimits.Markdown);
        draft.ChallengeQuestionMarkdown = new string('q', InputLimits.Markdown);
        var criterion = await App.Db(ctx => ctx.RubricCriteria.Where(c => c.WorkspaceId == draft.WorkspaceId).Select(c => c.Id).FirstAsync());
        var draftScore = NewScore(draft, criterion, score);
        draftScore.CommentMarkdown = new string('c', InputLimits.Markdown);
        draft.CriterionScores.Add(draftScore);
        await InsertDraftAsync(draft);

        var saved = await App.Db(ctx => ctx.ReviewDrafts.AsNoTracking().Include(d => d.CriterionScores).SingleAsync(d => d.Id == draft.Id));
        saved.Verdict.ShouldBe(verdict);
        saved.StrengthsMarkdown.ShouldBe(draft.StrengthsMarkdown);
        saved.ImprovementsMarkdown.ShouldBe(draft.ImprovementsMarkdown);
        saved.ChallengeQuestionMarkdown.ShouldBe(draft.ChallengeQuestionMarkdown);
        saved.CriterionScores.Single().Score.ShouldBe(score);
        saved.CriterionScores.Single().CommentMarkdown.Length.ShouldBe(InputLimits.Markdown);
        var storedVerdict = await App.Db(ctx => ctx.Database.SqlQuery<string>(
            $"SELECT \"Verdict\" AS \"Value\" FROM \"ReviewDrafts\" WHERE \"Id\" = {draft.Id}").SingleAsync());
        storedVerdict.ShouldBe(verdict.ToString());
    }

    [Fact]
    public async Task Draft_rejects_second_draft_for_submission()
    {
        var draft = await NewDraftAsync();
        await InsertDraftAsync(draft);
        draft.Id = Guid.CreateVersion7();

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertDraftAsync(draft));
        AssertConstraint(error, PostgresErrorCodes.UniqueViolation, "IX_ReviewDrafts_SubmissionId");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_rejects_missing_or_foreign_workspace_submission(bool foreign)
    {
        var draft = await NewDraftAsync();
        if (foreign) draft.WorkspaceId = (await Data.MentorAsync()).WorkspaceId;
        else draft.SubmissionId = Guid.NewGuid();

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertDraftAsync(draft));
        AssertConstraint(error, PostgresErrorCodes.ForeignKeyViolation, "FK_ReviewDrafts_Submissions_WorkspaceId_SubmissionId");
    }

    [Theory]
    [InlineData("version", "CK_ReviewDrafts_Version")]
    [InlineData("updated", "CK_ReviewDrafts_UpdatedAtUtc")]
    [InlineData("verdict", "CK_ReviewDrafts_Verdict")]
    public async Task Draft_rejects_invalid_values(string field, string constraint)
    {
        var draft = await NewDraftAsync();
        switch (field)
        {
            case "version": draft.Version = -1; break;
            case "updated": draft.UpdatedAtUtc = draft.CreatedAtUtc.AddTicks(-10); break;
            case "verdict": draft.Verdict = (ReviewVerdict)999; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertDraftAsync(draft));
        AssertConstraint(error, PostgresErrorCodes.CheckViolation, constraint);
    }

    [Theory]
    [InlineData(nameof(ReviewDraft.StrengthsMarkdown))]
    [InlineData(nameof(ReviewDraft.ImprovementsMarkdown))]
    [InlineData(nameof(ReviewDraft.ChallengeQuestionMarkdown))]
    public async Task Draft_rejects_markdown_over_limit(string field)
    {
        var draft = await NewDraftAsync();
        var text = new string('x', InputLimits.Markdown + 1);
        switch (field)
        {
            case nameof(ReviewDraft.StrengthsMarkdown): draft.StrengthsMarkdown = text; break;
            case nameof(ReviewDraft.ImprovementsMarkdown): draft.ImprovementsMarkdown = text; break;
            case nameof(ReviewDraft.ChallengeQuestionMarkdown): draft.ChallengeQuestionMarkdown = text; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertDraftAsync(draft));
        error.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Draft_score_rejects_value_outside_scale(short value)
    {
        var draft = await NewDraftAsync();
        var criterion = await App.Db(ctx => ctx.RubricCriteria.Where(c => c.WorkspaceId == draft.WorkspaceId).Select(c => c.Id).FirstAsync());
        draft.CriterionScores.Add(NewScore(draft, criterion, value));

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertDraftAsync(draft));
        AssertConstraint(error, PostgresErrorCodes.CheckViolation, "CK_ReviewDraftCriterionScores_Score");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_score_rejects_foreign_criterion_or_draft(bool foreignDraft)
    {
        var draft = await NewDraftAsync();
        await InsertDraftAsync(draft);
        var foreignMentor = await Data.MentorAsync();
        var foreignCriterion = (await Data.ScoresAsync(foreignMentor)).First().CriterionId;
        var score = NewScore(draft, foreignCriterion, 3);
        if (foreignDraft) score.WorkspaceId = foreignMentor.WorkspaceId;

        var error = await Should.ThrowAsync<DbUpdateException>(() => InsertScoreAsync(score));
        AssertConstraint(error, PostgresErrorCodes.ForeignKeyViolation, foreignDraft
            ? "FK_ReviewDraftCriterionScores_ReviewDrafts_WorkspaceId_DraftId"
            : "FK_ReviewDraftCriterionScores_RubricCriteria_WorkspaceId_Crite~");
    }

    [Fact]
    public async Task Draft_scores_reject_duplicates_and_overlong_comments()
    {
        var draft = await NewDraftAsync();
        await InsertDraftAsync(draft);
        var criterion = await App.Db(ctx => ctx.RubricCriteria.Where(c => c.WorkspaceId == draft.WorkspaceId).Select(c => c.Id).FirstAsync());
        await InsertScoreAsync(NewScore(draft, criterion, 3));
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => InsertScoreAsync(NewScore(draft, criterion, 4)));
        AssertConstraint(duplicate, PostgresErrorCodes.UniqueViolation, "PK_ReviewDraftCriterionScores");

        await App.Db(ctx => ctx.ReviewDraftCriterionScores.Where(s => s.DraftId == draft.Id).ExecuteDeleteAsync());
        var overlong = NewScore(draft, criterion, 3);
        overlong.CommentMarkdown = new string('x', InputLimits.Markdown + 1);
        var lengthError = await Should.ThrowAsync<DbUpdateException>(() => InsertScoreAsync(overlong));
        lengthError.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);
    }

    [Fact]
    public async Task Draft_concurrency_token_rejects_stale_update()
    {
        var draft = await NewDraftAsync();
        await InsertDraftAsync(draft);
        await App.Db(async stale =>
        {
            var row = await stale.ReviewDrafts.SingleAsync(d => d.Id == draft.Id);
            await App.Db(async current =>
            {
                var latest = await current.ReviewDrafts.SingleAsync(d => d.Id == draft.Id);
                latest.StrengthsMarkdown = "Current draft";
                latest.Version++;
                return await current.SaveChangesAsync();
            });
            row.StrengthsMarkdown = "Stale draft";
            row.Version++;
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            return 0;
        });
        (await App.Db(ctx => ctx.ReviewDrafts.Where(d => d.Id == draft.Id).Select(d => d.StrengthsMarkdown).SingleAsync())).ShouldBe("Current draft");
    }

    private async Task<MentorshipReviewConsent> NewConsentAsync()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        return NewConsent(mentor.WorkspaceId, mentee.MentorshipId);
    }

    private MentorshipReviewConsent NewConsent(Guid workspaceId, Guid mentorshipId) => new()
    {
        Id = Guid.CreateVersion7(), WorkspaceId = workspaceId, MentorshipId = mentorshipId,
        DisclosureVersion = new string('v', 100), GrantedAtUtc = App.Clock.GetUtcNow().UtcDateTime, Version = 1,
    };

    private async Task<ReviewDraft> NewDraftAsync()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        return new ReviewDraft
        {
            Id = Guid.CreateVersion7(), WorkspaceId = mentor.WorkspaceId, SubmissionId = submission.SubmissionId,
            CreatedAtUtc = App.Clock.GetUtcNow().UtcDateTime, UpdatedAtUtc = App.Clock.GetUtcNow().UtcDateTime, Version = 1,
        };
    }

    private static ReviewDraftCriterionScore NewScore(ReviewDraft draft, Guid criterionId, short score) => new()
    {
        WorkspaceId = draft.WorkspaceId, DraftId = draft.Id, CriterionId = criterionId, Score = score,
    };

    private Task<int> InsertConsentAsync(MentorshipReviewConsent consent) => App.Db(async ctx =>
    {
        ctx.MentorshipReviewConsents.Add(consent);
        return await ctx.SaveChangesAsync();
    });

    private Task<int> InsertDraftAsync(ReviewDraft draft) => App.Db(async ctx =>
    {
        ctx.ReviewDrafts.Add(draft);
        return await ctx.SaveChangesAsync();
    });

    private Task<int> InsertScoreAsync(ReviewDraftCriterionScore score) => App.Db(async ctx =>
    {
        ctx.ReviewDraftCriterionScores.Add(score);
        return await ctx.SaveChangesAsync();
    });

    private static void AssertConstraint(DbUpdateException error, string sqlState, string constraint)
    {
        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(sqlState);
        postgres.ConstraintName.ShouldBe(constraint);
    }
}
