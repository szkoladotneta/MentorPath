using System.Text.Json;
using MentorPath.Application.Common;
using MentorPath.Application.Common.Storage;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Privacy.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Cli;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MentorPath.IntegrationTests.Privacy;

public sealed class PersonalDataTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IPersonalDataService Privacy => App.Get<IPersonalDataService>();

    internal sealed record Scenario(
        MentorRef Mentor, MenteeRef A, MenteeRef B, MentorRef OtherMentor, MenteeRef C,
        Guid FirstAttemptId, Guid AcceptedAttemptId, Guid PendingAttemptId, string AttachmentKey, Guid BSubmissionId);

    /// <summary>
    /// Mentee A (workspace W1): onboarding, mentor private notes, a decision attempt with NeedsRevision + reflection,
    /// a second attempt Accepted, and a pending practical task with a file. Mentee B in W1 and mentee C in W2 have their own data.
    /// </summary>
    internal static async Task<Scenario> BuildAsync(TestApp app)
    {
        var data = app.Data;
        var mentor = await data.MentorAsync();
        var a = await data.MenteeAsync(mentor);
        var b = await data.MenteeAsync(mentor);
        var otherMentor = await data.MentorAsync();
        var c = await data.MenteeAsync(otherMentor);

        (await app.Get<IMentorshipService>().SaveOnboardingAsync(a.UserId, a.MentorshipId,
            new("Cel A sekret", ExperienceLevel.Mid, "Wyzwania A sekret", 6), default)).ShouldSucceed();
        (await app.Get<IMentorshipService>().UpdatePrivateNotesAsync(mentor.UserId, a.MentorshipId, "Notatka mentora o A", default)).ShouldSucceed();

        var decision = await data.PublishedAssignmentAsync(mentor, a, ExerciseType.ArchitecturalDecision);
        var first = await data.DecisionSubmissionAsync(a, decision.AssignmentId);
        var rubric = (await app.Get<IRubricService>().ListCriteriaAsync(mentor.UserId, false, default)).ShouldSucceed();
        (await app.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(first.SubmissionId, "Mocne A1", "Popraw A1", "Pytanie A1", ReviewVerdict.NeedsRevision, rubric
                .Select(c => new CriterionScoreInput(c.CriterionId, 2, "Komentarz do kryterium A sekret")).ToList()), default)).ShouldSucceed();
        (await app.Get<IReflectionService>().SaveReflectionAsync(a.UserId, new(first.SubmissionId, 4, "Wniosek A sekret"), default)).ShouldSucceed();
        app.Clock.Advance(TimeSpan.FromHours(1));
        var accepted = await data.DecisionSubmissionAsync(a, decision.AssignmentId);
        (await app.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(accepted.SubmissionId, "Mocne A2", "Popraw A2", "Pytanie A2", ReviewVerdict.Accepted, await data.ScoresAsync(mentor)), default)).ShouldSucceed();

        app.Clock.Advance(TimeSpan.FromHours(1));
        var practical = await data.PublishedAssignmentAsync(mentor, a, ExerciseType.PracticalTask);
        var bytes = "Notatki A sekret"u8.ToArray();
        var pending = (await app.Get<ISubmissionService>().SubmitPracticalAsync(a.UserId, new(practical.AssignmentId, Guid.NewGuid(),
            "Uzasadnienie A sekret", 4, "https://example.com/a-sekret", new FileUpload("notatki-a.txt", "text/plain", bytes.Length, new MemoryStream(bytes))),
            default)).ShouldSucceed();
        var key = await app.Db(db => db.SubmissionAttachments.Where(x => x.SubmissionId == pending.SubmissionId).Select(x => x.ObjectKey).SingleAsync());

        var bAssignment = await data.PublishedAssignmentAsync(mentor, b, ExerciseType.ArchitecturalDecision);
        var bSubmission = await data.DecisionSubmissionAsync(b, bAssignment.AssignmentId);
        (await app.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId,
            new(bSubmission.SubmissionId, "Mocne B", "Popraw B", "Pytanie B", ReviewVerdict.Accepted, await data.ScoresAsync(mentor)), default)).ShouldSucceed();
        (await app.Get<IReflectionService>().SaveReflectionAsync(b.UserId, new(bSubmission.SubmissionId, 5, "Wniosek B"), default)).ShouldSucceed();

        var cAssignment = await data.PublishedAssignmentAsync(otherMentor, c, ExerciseType.ArchitecturalDecision);
        await data.DecisionSubmissionAsync(c, cAssignment.AssignmentId);

        return new Scenario(mentor, a, b, otherMentor, c, first.SubmissionId, accepted.SubmissionId, pending.SubmissionId, key, bSubmission.SubmissionId);
    }

    // ---------- Export ----------

    [Fact]
    public async Task Export_contains_all_personal_data_of_the_mentee()
    {
        var s = await BuildAsync(App);

        var export = (await Privacy.ExportAsync(new(null, s.A.Email.ToUpperInvariant()), default)).ShouldSucceed();

        export.SchemaVersion.ShouldBe(2);
        export.Profile.UserId.ShouldBe(s.A.UserId);
        export.Profile.Email.ShouldBe(s.A.Email);
        export.Profile.Roles.ShouldBe([AppRoles.Mentee]);
        var mentorship = export.Mentorships.ShouldHaveSingleItem();
        (mentorship.MentorshipId, mentorship.Goal, mentorship.Challenges, mentorship.WeeklyHours, mentorship.CurrentLevel)
            .ShouldBe((s.A.MentorshipId, "Cel A sekret", "Wyzwania A sekret", (int?)6, (ExperienceLevel?)ExperienceLevel.Mid));

        export.Submissions.Select(x => x.SubmissionId).ShouldBe([s.FirstAttemptId, s.AcceptedAttemptId, s.PendingAttemptId]);
        var first = export.Submissions[0];
        first.DecisionAnswerJson.ShouldNotBeNull().ShouldContain("Modularny monolit");
        var feedback = first.Feedback.ShouldNotBeNull();
        (feedback.Verdict, feedback.StrengthsMarkdown, feedback.ImprovementsMarkdown, feedback.ChallengeQuestionMarkdown)
            .ShouldBe((ReviewVerdict.NeedsRevision, "Mocne A1", "Popraw A1", "Pytanie A1"));
        var reflection = first.Reflection.ShouldNotBeNull();
        (reflection.ConfidenceAfter, reflection.TakeawayMarkdown).ShouldBe(((short)4, "Wniosek A sekret"));
        export.Submissions[1].Feedback.ShouldNotBeNull().Verdict.ShouldBe(ReviewVerdict.Accepted);
        feedback.CriterionScores.ShouldNotBeNull().Count.ShouldBe(6);
        feedback.CriterionScores.ShouldAllBe(x => x.Score == 2 && x.CommentMarkdown == "Komentarz do kryterium A sekret");
        feedback.CriterionScores.Select(x => x.Position).ShouldBe([0, 1, 2, 3, 4, 5]);
        var pending = export.Submissions[2];
        (pending.Status, pending.AnswerMarkdown, pending.ExternalUrl).ShouldBe((SubmissionStatus.PendingReview, "Uzasadnienie A sekret", "https://example.com/a-sekret"));
        var attachment = pending.Attachment.ShouldNotBeNull();
        (attachment.ObjectKey, attachment.OriginalFileName, attachment.ContentType).ShouldBe((s.AttachmentKey, "notatki-a.txt", "text/plain"));
        export.Invitations.ShouldHaveSingleItem().AcceptedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Export_contains_no_secrets_private_notes_or_data_of_other_people()
    {
        var s = await BuildAsync(App);
        var stamp = await App.Db(db => db.Users.Where(u => u.Id == s.A.UserId).Select(u => u.SecurityStamp).SingleAsync());

        var export = (await Privacy.ExportAsync(new(s.A.UserId, null), default)).ShouldSucceed();
        var json = JsonSerializer.Serialize(export, PrivacyCommands.ExportJson);

        string[] forbidden =
        [
            "passwordHash", "securityStamp", "tokenHash", stamp!,
            "Notatka mentora o A", "Guide AD", "Guide PT", "mentorGuide", "isCorrect",
            s.Mentor.Email, s.B.Email, s.C.Email, s.OtherMentor.Email,
            s.B.UserId.ToString(), s.BSubmissionId.ToString(), "Mocne B", "Wniosek B",
        ];
        foreach (var value in forbidden) json.ShouldNotContain(value, Case.Insensitive, value);
        typeof(MentorshipExport).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Private"));
    }

    [Fact]
    public async Task Export_is_read_only()
    {
        var s = await BuildAsync(App);
        var before = await SubjectFullSnapshotAsync(App, s.A);
        var keys = App.Storage.Keys.Order().ToList();

        (await Privacy.ExportAsync(new(s.A.UserId, null), default)).ShouldSucceed();

        (await SubjectFullSnapshotAsync(App, s.A)).ShouldBe(before);
        App.Storage.Keys.Order().ToList().ShouldBe(keys);
    }

    // ---------- Subject resolution (isolation matrix) ----------

    [Fact]
    public async Task Unknown_subject_gets_NotFound_and_nothing_changes()
    {
        var s = await BuildAsync(App);
        var before = await SubjectFullSnapshotAsync(App, s.A);
        var unknownEmail = $"nobody-{Guid.NewGuid():N}"[..14] + "@test.local";

        (await Privacy.ExportAsync(new(Guid.NewGuid(), null), default)).ShouldBeNotFound();
        (await Privacy.ExportAsync(new(null, unknownEmail), default)).ShouldBeNotFound();
        (await Privacy.AnonymizeAsync(new(Guid.NewGuid(), null), true, default)).ShouldBeNotFound();
        (await Privacy.AnonymizeAsync(new(null, unknownEmail), true, default)).ShouldBeNotFound();

        (await SubjectFullSnapshotAsync(App, s.A)).ShouldBe(before);
    }

    [Fact]
    public async Task Subject_must_be_given_by_exactly_one_identifier()
    {
        var s = await BuildAsync(App);
        (await Privacy.ExportAsync(new(null, null), default)).ShouldFail(ErrorKind.Validation, "privacy.invalid_request");
        (await Privacy.ExportAsync(new(s.A.UserId, s.A.Email), default)).ShouldFail(ErrorKind.Validation, "privacy.invalid_request");
        (await Privacy.AnonymizeAsync(new(Guid.Empty, "  "), true, default)).ShouldFail(ErrorKind.Validation, "privacy.invalid_request");
    }

    [Fact]
    public async Task Workspace_owner_is_refused_even_when_also_a_mentee_elsewhere()
    {
        var s = await BuildAsync(App);
        await Data.MenteeAsync(s.OtherMentor, s.Mentor.Email, s.Mentor.UserId);
        var before = await OthersSnapshotAsync(s, excludedUserId: Guid.Empty);

        (await Privacy.ExportAsync(new(s.Mentor.UserId, null), default)).ShouldFail(ErrorKind.Validation, "privacy.unsupported_subject");
        (await Privacy.AnonymizeAsync(new(null, s.Mentor.Email), true, default)).ShouldFail(ErrorKind.Validation, "privacy.unsupported_subject");
        (await Privacy.AnonymizeAsync(new(s.OtherMentor.UserId, null), true, default)).ShouldFail(ErrorKind.Validation, "privacy.unsupported_subject");

        (await OthersSnapshotAsync(s, excludedUserId: Guid.Empty)).ShouldBe(before);
    }

    // ---------- Anonymization ----------

    [Fact]
    public async Task Dry_run_reports_counts_and_changes_nothing()
    {
        var s = await BuildAsync(App);
        var before = await SubjectFullSnapshotAsync(App, s.A);
        var keys = App.Storage.Keys.Order().ToList();

        var report = (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: false, default)).ShouldSucceed();

        report.ShouldBe(new AnonymizationReport(s.A.UserId, Applied: false, AlreadyAnonymized: false, MentorshipsClosed: 1, MentorshipsScrubbed: 1,
            SubmissionsScrubbed: 3, AttachmentsDeleted: 1, FeedbackScrubbed: 2, ReflectionsScrubbed: 1, InvitationsScrubbed: 1, ProductFeedbackScrubbed: 0));
        (await SubjectFullSnapshotAsync(App, s.A)).ShouldBe(before);
        App.Storage.Keys.Order().ToList().ShouldBe(keys);
    }

    [Fact]
    public async Task Anonymization_locks_the_account_and_removes_identity_data()
    {
        var s = await BuildAsync(App);
        var users = App.Get<UserManager<AppUser>>();
        var stampBefore = (await users.FindByIdAsync(s.A.UserId.ToString()))!.SecurityStamp;

        (await Privacy.AnonymizeAsync(new(null, s.A.Email), apply: true, default)).ShouldSucceed().Applied.ShouldBeTrue();

        users = App.Get<UserManager<AppUser>>();
        (await users.FindByEmailAsync(s.A.Email)).ShouldBeNull();
        var user = (await users.FindByIdAsync(s.A.UserId.ToString())).ShouldNotBeNull();
        var placeholder = $"deleted-{s.A.UserId:N}@anonymized.invalid";
        (user.Email, user.UserName, user.DisplayName).ShouldBe((placeholder, placeholder, "Usunięty użytkownik"));
        user.NormalizedEmail.ShouldBe(users.NormalizeEmail(placeholder));
        (user.PasswordHash, user.PhoneNumber, user.EmailConfirmed).ShouldBe(((string?)null, (string?)null, false));
        user.SecurityStamp.ShouldNotBe(stampBefore);
        (await users.CheckPasswordAsync(user, TestDataBuilder.Password)).ShouldBeFalse();
        (await users.IsLockedOutAsync(user)).ShouldBeTrue();
        (await users.GetRolesAsync(user)).ShouldBeEmpty();
        (await users.GetLoginsAsync(user)).ShouldBeEmpty();
        (await users.GetClaimsAsync(user)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Anonymization_clears_personal_content_deletes_files_and_closes_mentorships()
    {
        var s = await BuildAsync(App);
        App.Clock.Advance(TimeSpan.FromDays(3));
        var now = App.Clock.GetUtcNow().UtcDateTime;

        var report = (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();

        report.ShouldBe(new AnonymizationReport(s.A.UserId, Applied: true, AlreadyAnonymized: false, MentorshipsClosed: 1, MentorshipsScrubbed: 1,
            SubmissionsScrubbed: 3, AttachmentsDeleted: 1, FeedbackScrubbed: 2, ReflectionsScrubbed: 1, InvitationsScrubbed: 1, ProductFeedbackScrubbed: 0));
        App.Storage.Keys.ShouldNotContain(s.AttachmentKey);
        var placeholderNormalized = App.Get<UserManager<AppUser>>().NormalizeEmail($"deleted-{s.A.UserId:N}@anonymized.invalid");
        await App.Db(async db =>
        {
            var m = await db.Mentorships.SingleAsync(x => x.Id == s.A.MentorshipId);
            (m.Status, m.UpdatedAtUtc, m.Goal, m.Challenges, m.MentorPrivateNotes).ShouldBe((MentorshipStatus.Closed, now, (string?)null, (string?)null, (string?)null));
            var subs = await db.Submissions.Where(x => x.MenteeUserId == s.A.UserId).ToListAsync();
            subs.Count.ShouldBe(3);
            subs.ShouldAllBe(x => x.AnswerMarkdown == null && x.DecisionAnswerJson == null && x.ExternalUrl == null);
            (await db.SubmissionAttachments.CountAsync(x => x.SubmissionId == s.PendingAttemptId)).ShouldBe(0);
            var feedback = await db.MentorFeedback.Where(x => x.Submission!.MenteeUserId == s.A.UserId).ToListAsync();
            feedback.Count.ShouldBe(2);
            feedback.ShouldAllBe(x => x.StrengthsMarkdown == "" && x.ImprovementsMarkdown == "" && x.ChallengeQuestionMarkdown == "");
            var scores = await db.FeedbackCriterionScores.Where(x => x.Feedback!.Submission!.MenteeUserId == s.A.UserId).ToListAsync();
            scores.Count.ShouldBe(12);
            scores.ShouldAllBe(x => x.CommentMarkdown == "");
            scores.Count(x => x.Score == 2).ShouldBe(6);
            (await db.SubmissionReflections.SingleAsync(x => x.SubmissionId == s.FirstAttemptId)).TakeawayMarkdown.ShouldBe("");
            (await db.Invitations.CountAsync(x => x.WorkspaceId == s.Mentor.WorkspaceId && x.NormalizedEmail == placeholderNormalized)).ShouldBe(1);
            (await db.Invitations.CountAsync(x => x.NormalizedEmail == s.A.Email.ToUpperInvariant())).ShouldBe(0);
            return 0;
        });

        var json = JsonSerializer.Serialize((await Privacy.ExportAsync(new(s.A.UserId, null), default)).ShouldSucceed(), PrivacyCommands.ExportJson);
        json.ShouldNotContain("sekret");
        json.ShouldNotContain(s.A.Email);
    }

    [Fact]
    public async Task Anonymization_preserves_workspace_statistics_and_progress()
    {
        var s = await BuildAsync(App);
        var statsBefore = await SubjectStatisticsAsync(s);

        (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();

        (await SubjectStatisticsAsync(s)).ShouldBe(statsBefore);
        var queue = (await App.Get<IReviewService>().GetPendingAsync(s.Mentor.UserId, default)).ShouldSucceed();
        queue.Single(x => x.SubmissionId == s.PendingAttemptId).MenteeDisplayName.ShouldBe("Usunięty użytkownik");
    }

    [Fact]
    public async Task Anonymization_does_not_touch_other_mentees_mentors_or_workspaces()
    {
        var s = await BuildAsync(App);
        var before = await OthersSnapshotAsync(s, s.A.UserId);
        var otherKeys = App.Storage.Keys.Where(k => k != s.AttachmentKey).Order().ToList();

        (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();

        (await OthersSnapshotAsync(s, s.A.UserId)).ShouldBe(before);
        App.Storage.Keys.Order().ToList().ShouldBe(otherKeys);
    }

    [Fact]
    public async Task Mentee_in_two_workspaces_is_exported_and_anonymized_in_both()
    {
        var s = await BuildAsync(App);
        var second = await Data.MenteeAsync(s.OtherMentor, s.A.Email, s.A.UserId);
        var before = await OthersSnapshotAsync(s, s.A.UserId);

        var export = (await Privacy.ExportAsync(new(s.A.UserId, null), default)).ShouldSucceed();
        export.Mentorships.Select(m => m.MentorshipId).ShouldBe([s.A.MentorshipId, second.MentorshipId], ignoreOrder: true);
        export.Invitations.Count.ShouldBe(2);

        var report = (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();
        (report.MentorshipsClosed, report.InvitationsScrubbed).ShouldBe((2, 2));
        (await App.Db(db => db.Mentorships.Where(m => m.MenteeUserId == s.A.UserId).Select(m => m.Status).ToListAsync()))
            .ShouldAllBe(x => x == MentorshipStatus.Closed);
        (await OthersSnapshotAsync(s, s.A.UserId)).ShouldBe(before);
    }

    [Fact]
    public async Task Rerun_is_idempotent_and_scrubs_feedback_written_after_the_first_run()
    {
        var s = await BuildAsync(App);
        (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();
        var stamp = await App.Db(db => db.Users.Where(u => u.Id == s.A.UserId).Select(u => u.SecurityStamp).SingleAsync());
        (await App.Get<IReviewService>().SubmitFeedbackAsync(s.Mentor.UserId,
            new(s.PendingAttemptId, "Po anonimizacji", "Po anonimizacji", "Po anonimizacji", ReviewVerdict.Accepted, await Data.ScoresAsync(s.Mentor)), default)).ShouldSucceed();

        var report = (await Privacy.AnonymizeAsync(new(s.A.UserId, null), apply: true, default)).ShouldSucceed();

        (report.AlreadyAnonymized, report.MentorshipsClosed, report.AttachmentsDeleted, report.FeedbackScrubbed).ShouldBe((true, 0, 0, 3));
        (await App.Db(db => db.Users.Where(u => u.Id == s.A.UserId).Select(u => u.SecurityStamp).SingleAsync())).ShouldBe(stamp);
        (await App.Db(db => db.MentorFeedback.SingleAsync(f => f.SubmissionId == s.PendingAttemptId))).StrengthsMarkdown.ShouldBe("");
    }

    [Fact]
    public async Task Real_account_in_the_placeholder_domain_is_still_anonymized()
    {
        var mentor = await Data.MentorAsync();
        var lookalike = await Data.MenteeAsync(mentor, $"real-{Guid.NewGuid():N}"[..13] + "@anonymized.invalid");

        var report = (await Privacy.AnonymizeAsync(new(lookalike.UserId, null), apply: true, default)).ShouldSucceed();

        report.AlreadyAnonymized.ShouldBeFalse();
        var user = (await App.Get<UserManager<AppUser>>().FindByIdAsync(lookalike.UserId.ToString()))!;
        (user.Email, user.PasswordHash, user.DisplayName).ShouldBe(($"deleted-{lookalike.UserId:N}@anonymized.invalid", (string?)null, "Usunięty użytkownik"));
    }

    [Fact]
    public async Task Storage_failure_leaves_the_database_unchanged_and_a_rerun_completes()
    {
        var storage = new FlakyStorage();
        await using var app = await TestApp.CreateAsync(Database, overrides: services => services.AddSingleton<IFileStorage>(storage));
        var s = await BuildAsync(app);
        var before = await SubjectFullSnapshotAsync(app, s.A);
        storage.FailDeletes = true;

        (await app.Get<IPersonalDataService>().AnonymizeAsync(new(s.A.UserId, null), true, default))
            .ShouldFail(ErrorKind.Unavailable, "privacy.storage_failed");

        (await SubjectFullSnapshotAsync(app, s.A)).ShouldBe(before);
        storage.FailDeletes = false;
        (await app.Get<IPersonalDataService>().AnonymizeAsync(new(s.A.UserId, null), true, default)).ShouldSucceed().AlreadyAnonymized.ShouldBeFalse();
        storage.Inner.Keys.ShouldNotContain(s.AttachmentKey);
        (await app.Db(db => db.SubmissionAttachments.CountAsync(x => x.SubmissionId == s.PendingAttemptId))).ShouldBe(0);
        (await app.Db(db => db.Users.SingleAsync(u => u.Id == s.A.UserId))).Email.ShouldBe($"deleted-{s.A.UserId:N}@anonymized.invalid");
    }

    // ---------- Snapshots ----------

    /// <summary>Every stored value of the subject (account and content), to prove a call changed nothing.</summary>
    internal static Task<string> SubjectFullSnapshotAsync(TestApp app, MenteeRef subject) => app.Db(async db =>
    {
        var id = subject.UserId;
        var subIds = await db.Submissions.Where(x => x.MenteeUserId == id).Select(x => x.Id).ToListAsync();
        var normalized = subject.Email.ToUpperInvariant();
        return JsonSerializer.Serialize(new
        {
            User = await db.Users.Where(u => u.Id == id)
                .Select(u => new { u.Email, u.NormalizedEmail, u.UserName, u.DisplayName, u.PasswordHash, u.SecurityStamp, u.LockoutEnd, u.ConcurrencyStamp }).SingleAsync(),
            Roles = await db.UserRoles.Where(r => r.UserId == id).OrderBy(r => r.RoleId).Select(r => r.RoleId).ToListAsync(),
            Mentorships = await db.Mentorships.Where(m => m.MenteeUserId == id).OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.Status, m.Goal, m.Challenges, m.MentorPrivateNotes, m.UpdatedAtUtc }).ToListAsync(),
            Submissions = await db.Submissions.Where(x => x.MenteeUserId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.AnswerMarkdown, x.DecisionAnswerJson, x.ExternalUrl, x.AttemptNumber }).ToListAsync(),
            Attachments = await db.SubmissionAttachments.Where(x => subIds.Contains(x.SubmissionId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ObjectKey, x.OriginalFileName }).ToListAsync(),
            Feedback = await db.MentorFeedback.Where(x => subIds.Contains(x.SubmissionId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StrengthsMarkdown, x.ImprovementsMarkdown, x.ChallengeQuestionMarkdown, x.Verdict }).ToListAsync(),
            Reflections = await db.SubmissionReflections.Where(x => subIds.Contains(x.SubmissionId)).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.TakeawayMarkdown, x.ConfidenceAfter }).ToListAsync(),
            Invitations = await db.Invitations.Where(x => x.NormalizedEmail == normalized).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.NormalizedEmail }).ToListAsync(),
        });
    });

    /// <summary>Values that workspace statistics and progress are computed from; anonymization must not change them.</summary>
    private async Task<string> SubjectStatisticsAsync(Scenario s)
    {
        var id = s.A.UserId;
        var stored = await App.Db(async db => JsonSerializer.Serialize(new
        {
            Submissions = await db.Submissions.Where(x => x.MenteeUserId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.WorkspaceId, x.AssignmentId, x.Status, x.AttemptNumber, x.ConfidenceBefore, x.SubmittedAtUtc, x.SelectedQuizOptionId }).ToListAsync(),
            Feedback = await db.MentorFeedback.Where(x => x.Submission!.MenteeUserId == id).OrderBy(x => x.Id)
                .Select(x => new { x.SubmissionId, x.MentorUserId, x.Verdict, x.CreatedAtUtc }).ToListAsync(),
            Reflections = await db.SubmissionReflections.Where(x => x.Submission!.MenteeUserId == id).OrderBy(x => x.Id)
                .Select(x => new { x.SubmissionId, x.ConfidenceAfter, x.CreatedAtUtc }).ToListAsync(),
            Mentorships = await db.Mentorships.Where(x => x.MenteeUserId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.WorkspaceId, x.WeeklyHours, x.CurrentLevel, x.CreatedAtUtc }).ToListAsync(),
        }));
        var progress = (await App.Get<IProgressService>().GetRoadmapProgressAsync(s.Mentor.UserId, s.A.MentorshipId, default)).ShouldSucceed();
        return stored + JsonSerializer.Serialize(progress);
    }

    /// <summary>All rows of everyone else involved: both mentors, mentees B and C, both workspaces.</summary>
    private Task<string> OthersSnapshotAsync(Scenario s, Guid excludedUserId) => App.Db(async db =>
    {
        Guid[] users = [s.Mentor.UserId, s.OtherMentor.UserId, s.B.UserId, s.C.UserId];
        Guid[] workspaces = [s.Mentor.WorkspaceId, s.OtherMentor.WorkspaceId];
        var excludedNormalized = s.A.Email.ToUpperInvariant();
        return JsonSerializer.Serialize(new
        {
            Users = await db.Users.Where(u => users.Contains(u.Id)).OrderBy(u => u.Id)
                .Select(u => new { u.Id, u.Email, u.NormalizedEmail, u.UserName, u.DisplayName, u.PasswordHash, u.SecurityStamp, u.LockoutEnd, u.ConcurrencyStamp }).ToListAsync(),
            Roles = await db.UserRoles.Where(r => users.Contains(r.UserId)).OrderBy(r => r.UserId).ThenBy(r => r.RoleId)
                .Select(r => new { r.UserId, r.RoleId }).ToListAsync(),
            Workspaces = await db.Workspaces.Where(w => workspaces.Contains(w.Id)).OrderBy(w => w.Id).Select(w => new { w.Id, w.Name, w.OwnerUserId }).ToListAsync(),
            Mentorships = await db.Mentorships.Where(m => workspaces.Contains(m.WorkspaceId) && m.MenteeUserId != excludedUserId).OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.MenteeUserId, m.Status, m.Goal, m.Challenges, m.MentorPrivateNotes, m.UpdatedAtUtc }).ToListAsync(),
            Submissions = await db.Submissions.Where(x => workspaces.Contains(x.WorkspaceId) && x.MenteeUserId != excludedUserId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Status, x.AnswerMarkdown, x.DecisionAnswerJson, x.ExternalUrl, x.AttemptNumber, x.ConfidenceBefore, x.SubmittedAtUtc }).ToListAsync(),
            Feedback = await db.MentorFeedback.Where(x => workspaces.Contains(x.WorkspaceId) && x.Submission!.MenteeUserId != excludedUserId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StrengthsMarkdown, x.ImprovementsMarkdown, x.ChallengeQuestionMarkdown, x.Verdict, x.CreatedAtUtc }).ToListAsync(),
            Reflections = await db.SubmissionReflections.Where(x => workspaces.Contains(x.Submission!.WorkspaceId) && x.Submission.MenteeUserId != excludedUserId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.TakeawayMarkdown, x.ConfidenceAfter }).ToListAsync(),
            Scores = await db.FeedbackCriterionScores.Where(x => workspaces.Contains(x.WorkspaceId) && x.Feedback!.Submission!.MenteeUserId != excludedUserId)
                .OrderBy(x => x.FeedbackId).ThenBy(x => x.CriterionId).Select(x => new { x.FeedbackId, x.CriterionId, x.Score, x.CommentMarkdown }).ToListAsync(),
            Attachments = await db.SubmissionAttachments.Where(x => workspaces.Contains(x.WorkspaceId) && x.Submission!.MenteeUserId != excludedUserId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ObjectKey, x.OriginalFileName }).ToListAsync(),
            Invitations = await db.Invitations.Where(x => workspaces.Contains(x.WorkspaceId) && x.NormalizedEmail != excludedNormalized
                    && !x.NormalizedEmail.StartsWith("DELETED-")).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.NormalizedEmail, x.AcceptedAtUtc, x.ExpiresAtUtc }).ToListAsync(),
            Roadmaps = await db.Roadmaps.Where(r => workspaces.Contains(r.WorkspaceId)).OrderBy(r => r.Id).Select(r => new { r.Id, r.Status }).ToListAsync(),
            Topics = await db.Topics.CountAsync(t => workspaces.Contains(t.WorkspaceId)),
            Exercises = await db.Exercises.CountAsync(e => workspaces.Contains(e.WorkspaceId)),
        });
    });

    private sealed class FlakyStorage : IFileStorage
    {
        public InMemoryFileStorage Inner { get; } = new();
        public bool FailDeletes { get; set; }

        public Task SaveAsync(string objectKey, Stream content, string contentType, CancellationToken ct) => Inner.SaveAsync(objectKey, content, contentType, ct);
        public Task<Stream> OpenReadAsync(string objectKey, CancellationToken ct) => Inner.OpenReadAsync(objectKey, ct);
        public IAsyncEnumerable<StoredBlobInfo> ListAsync(CancellationToken ct) => Inner.ListAsync(ct);

        public Task DeleteAsync(string objectKey, CancellationToken ct) =>
            FailDeletes ? throw new IOException("storage unavailable") : Inner.DeleteAsync(objectKey, ct);
    }
}
