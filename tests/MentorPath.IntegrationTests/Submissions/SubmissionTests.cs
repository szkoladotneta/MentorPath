using System.Text;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Submissions;

public sealed class SubmissionTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private ISubmissionService Submissions => App.Get<ISubmissionService>();

    private async Task<(Guid Correct, Guid Wrong)> OptionsAsync(MentorRef mentor, Guid exerciseId)
    {
        var dto = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentor.UserId, exerciseId, default)).ShouldSucceed();
        var ids = await App.Db(ctx => ctx.QuizOptions.Where(o => o.ExerciseId == exerciseId).OrderBy(o => o.Position).Select(o => new { o.Id, o.IsCorrect }).ToListAsync());
        dto.Content.QuizOptions.Count.ShouldBe(ids.Count);
        return (ids.First(o => o.IsCorrect).Id, ids.First(o => !o.IsCorrect).Id);
    }

    [Fact]
    public async Task Quiz_is_auto_graded_retryable_until_correct_and_reveals_explanation_only_when_correct()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var (correct, wrong) = await OptionsAsync(mentor, asg.ExerciseId);

        var first = (await Submissions.SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), wrong, "Bo tak", 4), default)).ShouldSucceed();
        first.Status.ShouldBe(SubmissionStatus.Incorrect);
        first.Quiz!.IsCorrect.ShouldBeFalse();
        first.Quiz.ExerciseExplanationMarkdown.ShouldBeNull();

        var second = (await App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), correct, "Teraz rozumiem", 3), default)).ShouldSucceed();
        second.Status.ShouldBe(SubmissionStatus.Correct);
        second.AttemptNumber.ShouldBe(2);
        second.Quiz!.ExerciseExplanationMarkdown.ShouldBe("Wyjaśnienie");

        (await App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), correct, "x", 3), default))
            .ShouldFail(ErrorKind.Conflict, "submission.already_completed");
    }

    [Fact]
    public async Task Quiz_requires_rationale_confidence_and_option_from_the_exercise()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var other = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var (foreignOption, _) = await OptionsAsync(mentor, other.ExerciseId);

        var e = (await Submissions.SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), Guid.Empty, " ", 0), default)).ShouldFail(ErrorKind.Validation);
        e.FieldErrors!.Keys.ShouldBe(["RationaleMarkdown", "ConfidenceBefore", "SelectedQuizOptionId"], ignoreOrder: true);
        (await App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), foreignOption, "x", 3), default))
            .ShouldFail(ErrorKind.Validation, "submission.invalid_option");
    }

    [Fact]
    public async Task Same_client_request_id_never_creates_two_attempts_even_concurrently()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var (_, wrong) = await OptionsAsync(mentor, asg.ExerciseId);
        var requestId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, requestId, wrong, "x", 2), default)));
        results.ShouldAllBe(r => r.IsSuccess);
        results.Select(r => r.Value!.SubmissionId).Distinct().Count().ShouldBe(1);
        (await App.Db(ctx => ctx.Submissions.CountAsync(s => s.AssignmentId == asg.AssignmentId))).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_different_requests_create_unique_attempt_numbers()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        var (_, wrong) = await OptionsAsync(mentor, asg.ExerciseId);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), wrong, "x", 2), default)));
        var attempts = await App.Db(ctx => ctx.Submissions.Where(s => s.AssignmentId == asg.AssignmentId).Select(s => s.AttemptNumber).ToListAsync());
        attempts.Count.ShouldBe(results.Count(r => r.IsSuccess));
        attempts.Distinct().Count().ShouldBe(attempts.Count);
        results.Where(r => !r.IsSuccess).ShouldAllBe(r => r.Error!.Code == "submission.concurrent_attempt");
    }

    [Fact]
    public async Task Decision_requires_all_six_fields_and_blocks_next_attempt_while_pending()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);

        var e = (await Submissions.SubmitDecisionAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "", "", "", ""), 3), default))
            .ShouldFail(ErrorKind.Validation);
        e.FieldErrors!.Count.ShouldBe(4);

        var first = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        first.Status.ShouldBe(SubmissionStatus.PendingReview);
        (await App.Get<ISubmissionService>().SubmitDecisionAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default))
            .ShouldFail(ErrorKind.Conflict, "submission.pending_review_exists");

        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        history.Attempts.Single().DecisionAnswer!.TradeOffs.ShouldBe("Prostota vs skalowanie");
        var json = await App.Db(ctx => ctx.Submissions.Where(s => s.Id == first.SubmissionId).Select(s => s.DecisionAnswerJson).SingleAsync());
        json!.Replace(" ", "").ShouldContain("\"schemaVersion\":1");
    }

    [Fact]
    public async Task Wrong_form_for_exercise_type_is_rejected()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.QuizSingleChoice);
        (await Submissions.SubmitDecisionAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default))
            .ShouldFail(ErrorKind.Validation, "submission.wrong_type");
    }

    [Fact]
    public async Task Practical_task_accepts_link_or_file_and_validates_file()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.PracticalTask);

        (await Submissions.SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "Uzasadnienie", 3, null, null), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.ShouldContainKey("ExternalUrl");
        (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "Uzasadnienie", 3, "javascript:alert(1)", null), default))
            .ShouldFail(ErrorKind.Validation);
        var exe = new FileUpload("malware.exe", "application/octet-stream", 4, new MemoryStream([0x4D, 0x5A, 0, 0]));
        (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "U", 3, null, exe), default))
            .ShouldFail(ErrorKind.Validation).FieldErrors!.ShouldContainKey("File");
        var fakePdf = new FileUpload("a.pdf", "application/pdf", 4, new MemoryStream("abcd"u8.ToArray()));
        (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "U", 3, null, fakePdf), default))
            .ShouldFail(ErrorKind.Validation);
        var big = new FileUpload("big.txt", "text/plain", InputLimits.MaxAttachmentBytes + 1, new MemoryStream(new byte[InputLimits.MaxAttachmentBytes + 1]));
        (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "U", 3, null, big), default))
            .ShouldFail(ErrorKind.Validation);
        App.Storage.Keys.ShouldBeEmpty();

        var pdf = new FileUpload("adr.pdf", "application/pdf", 9, new MemoryStream("%PDF-1.7\n"u8.ToArray()));
        var ok = (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId,
            new(asg.AssignmentId, Guid.NewGuid(), "Uzasadnienie", 4, "https://github.com/x/y", pdf), default)).ShouldSucceed();
        ok.Status.ShouldBe(SubmissionStatus.PendingReview);
        var key = App.Storage.Keys.Single();
        key.ShouldStartWith($"ws/{mentor.WorkspaceId:N}/sub/");
        key.ShouldNotContain("adr");
    }

    [Fact]
    public async Task Attachment_is_downloadable_only_by_author_and_workspace_owner()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.PracticalTask);
        var md = new FileUpload("notes.md", "text/markdown", 8, new MemoryStream(Encoding.UTF8.GetBytes("# ADR ok")));
        (await Submissions.SubmitPracticalAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), "U", 4, null, md), default)).ShouldSucceed();
        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        var attachmentId = history.Attempts.Single().Attachment!.AttachmentId;

        var own = (await App.Get<ISubmissionService>().OpenAttachmentAsync(mentee.UserId, attachmentId, default)).ShouldSucceed();
        own.FileName.ShouldBe("notes.md");
        (await App.Get<ISubmissionService>().OpenAttachmentAsync(a.UserId, attachmentId, default)).ShouldSucceed();
        (await App.Get<ISubmissionService>().OpenAttachmentAsync(b.UserId, attachmentId, default)).ShouldBeNotFound();
        (await App.Get<ISubmissionService>().OpenAttachmentAsync(other.UserId, attachmentId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task History_is_private_to_mentee_and_workspace_owner()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);

        (await Submissions.GetHistoryAsync(a.UserId, asg.AssignmentId, default)).ShouldSucceed().Attempts.Count.ShouldBe(1);
        (await App.Get<ISubmissionService>().GetHistoryAsync(b.UserId, asg.AssignmentId, default)).ShouldBeNotFound();
        (await App.Get<ISubmissionService>().GetHistoryAsync(other.UserId, asg.AssignmentId, default)).ShouldBeNotFound();
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(other.UserId, asg.AssignmentId, default)).ShouldBeNotFound();
        (await App.Get<ISubmissionService>().SubmitDecisionAsync(other.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default)).ShouldBeNotFound();
        (await App.Get<ISubmissionService>().SubmitDecisionAsync(a.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Paused_mentorship_and_withdrawn_assignment_block_new_attempts()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);

        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Paused, default)).ShouldSucceed();
        (await App.Get<ISubmissionService>().GetAssignmentForMenteeAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed()
            .SubmitBlockReason.ShouldBe(SubmitBlockReason.MentorshipNotActive);
        (await App.Get<ISubmissionService>().SubmitDecisionAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default))
            .ShouldFail(ErrorKind.Conflict, "submission.mentorship_not_active");

        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Active, default)).ShouldSucceed();
        (await App.Get<MentorPath.Application.Roadmaps.Contracts.IRoadmapService>().SetAssignmentActiveAsync(mentor.UserId, asg.AssignmentId, false, default)).ShouldSucceed();
        (await App.Get<ISubmissionService>().SubmitDecisionAsync(mentee.UserId, new(asg.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default))
            .ShouldFail(ErrorKind.Conflict, "submission.assignment_inactive");
    }

    [Fact]
    public async Task Submitted_attempt_and_feedback_remain_unchanged_after_new_attempt()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var asg = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var first = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentor.UserId, new(first.SubmissionId, "S", "I", "Q?", ReviewVerdict.NeedsRevision), default)).ShouldSucceed();
        await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);

        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        history.Attempts.Select(a => a.AttemptNumber).ShouldBe([2, 1]);
        var old = history.Attempts.Single(a => a.AttemptNumber == 1);
        old.Status.ShouldBe(SubmissionStatus.NeedsRevision);
        old.Feedback!.ImprovementsMarkdown.ShouldBe("I");
        old.DecisionAnswer!.Decision.ShouldBe("Modularny monolit");
    }
}
