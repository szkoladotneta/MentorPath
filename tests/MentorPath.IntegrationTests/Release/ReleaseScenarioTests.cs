using MentorPath.Application.AiAuthoring.Contracts;
using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Progress.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Release;

/// <summary>The MVP release gate from docs/DOMAIN_RULES.md §9, executed end-to-end through the application services.</summary>
public sealed class ReleaseScenarioTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Full_mentoring_cycle_with_two_isolated_mentors()
    {
        // 1. Operator creates Mentor A and Mentor B.
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        mentorA.WorkspaceId.ShouldNotBe(mentorB.WorkspaceId);

        // 2. Mentor A invites a mentee; the mentee activates the account and fills onboarding.
        var invite = (await App.Get<IInvitationService>().CreateAsync(mentorA.UserId, new("release.mentee@test.local"), default)).ShouldSucceed();
        var accepted = (await App.Get<IInvitationService>().AcceptWithNewAccountAsync(
            new(TestDataBuilder.TokenOf(invite.AcceptUrl), "Release Mentee", TestDataBuilder.Password), default)).ShouldSucceed();
        var mentee = new MenteeRef(accepted.UserId, accepted.MentorshipId, "release.mentee@test.local");
        (await App.Get<IMentorshipService>().SaveOnboardingAsync(mentee.UserId, mentee.MentorshipId,
            new("Decyzje architektoniczne", ExperienceLevel.Mid, "Spójność danych", 6), default)).ShouldSucceed();

        // 3. Mentor A generates an exercise draft with AI.
        var topicDecisions = await Data.TopicAsync(mentorA, "Decyzje");
        var ai = (await App.Get<IAiExerciseDraftService>().GenerateDraftAsync(mentorA.UserId,
            new(topicDecisions, ExerciseType.ArchitecturalDecision, Difficulty.Intermediate, "Analiza kompromisów", null, 60), default)).ShouldSucceed();
        var draft = (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(mentorA.UserId, ai.ExerciseId, default)).ShouldSucceed();
        draft.Status.ShouldBe(ExerciseStatus.Draft);

        // 4. Mentor edits and publishes the exercise.
        (await App.Get<IExerciseAuthoringService>().UpdateDraftAsync(mentorA.UserId, ai.ExerciseId,
            draft.Content with { Title = "Podział modułów — wersja mentora" }, default)).ShouldSucceed();
        (await App.Get<IExerciseAuthoringService>().PublishAsync(mentorA.UserId, ai.ExerciseId, default)).ShouldSucceed();

        // 5. Mentor creates and publishes a roadmap with three topics (quiz, ADR, practical).
        var topicQuiz = await Data.TopicAsync(mentorA, "Quiz");
        var topicPractical = await Data.TopicAsync(mentorA, "Praktyka");
        var quizExercise = await Data.PublishedExerciseAsync(mentorA, topicQuiz, ExerciseType.QuizSingleChoice);
        var practicalExercise = await Data.PublishedExerciseAsync(mentorA, topicPractical, ExerciseType.PracticalTask);
        var roadmaps = App.Get<IRoadmapService>();
        var roadmapId = (await roadmaps.CreateAsync(mentorA.UserId, new(mentee.MentorshipId, "Release roadmap", "Cel"), default)).ShouldSucceed();
        var itemQuiz = (await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapId, topicQuiz, null), default)).ShouldSucceed();
        var itemDecision = (await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapId, topicDecisions, "Najważniejszy temat"), default)).ShouldSucceed();
        var itemPractical = (await roadmaps.AddTopicAsync(mentorA.UserId, new(roadmapId, topicPractical, null), default)).ShouldSucceed();
        var aQuiz = (await roadmaps.AssignExerciseAsync(mentorA.UserId, new(itemQuiz, quizExercise, true, null), default)).ShouldSucceed();
        var aDecision = (await roadmaps.AssignExerciseAsync(mentorA.UserId, new(itemDecision, ai.ExerciseId, true, null), default)).ShouldSucceed();
        var aPractical = (await roadmaps.AssignExerciseAsync(mentorA.UserId, new(itemPractical, practicalExercise, true, null), default)).ShouldSucceed();
        (await roadmaps.PublishAsync(mentorA.UserId, roadmapId, default)).ShouldSucceed();
        (await App.Get<IRoadmapService>().GetForMenteeAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().Items.Count.ShouldBe(3);

        // 6. Mentee solves the quiz.
        var options = (await App.Get<IExerciseAuthoringService>().GetMenteeExerciseAsync(mentee.UserId, aQuiz, default)).ShouldSucceed().QuizOptions;
        var correctOptionId = await App.Db(ctx => ctx.QuizOptions.Where(o => o.ExerciseId == quizExercise && o.IsCorrect).Select(o => o.Id).SingleAsync());
        options.ShouldContain(o => o.QuizOptionId == correctOptionId);
        (await App.Get<ISubmissionService>().SubmitQuizAsync(mentee.UserId, new(aQuiz, Guid.NewGuid(), correctOptionId, "Jedna transakcja", 4), default))
            .ShouldSucceed().Status.ShouldBe(SubmissionStatus.Correct);

        // 7. Mentee submits an ADR and a repository with attachment.
        var adr1 = await Data.DecisionSubmissionAsync(mentee, aDecision);
        var practical = (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId, new(aPractical, Guid.NewGuid(), "Repozytorium", 3,
            "https://github.com/mentee/repo", new FileUpload("adr.md", "text/markdown", 5, new MemoryStream("# ADR"u8.ToArray()))), default)).ShouldSucceed();

        // 8. Mentor sends the ADR back for revision.
        (await App.Get<IReviewService>().GetPendingAsync(mentorA.UserId, default)).ShouldSucceed().Count.ShouldBe(2);
        var scoresA = await Data.ScoresAsync(mentorA);
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentorA.UserId, new(adr1.SubmissionId, "Dobre alternatywy", "Brak ryzyk", "Co przy 10x ruchu?", ReviewVerdict.NeedsRevision, scoresA), default)).ShouldSucceed();
        (await App.Get<IReflectionService>().SaveReflectionAsync(mentee.UserId, new(adr1.SubmissionId, 3, "Muszę opisywać ryzyka"), default)).ShouldSucceed();

        // 9. Mentee improves the solution.
        var adr2 = await Data.DecisionSubmissionAsync(mentee, aDecision);
        adr2.AttemptNumber.ShouldBe(2);

        // 10. Mentor accepts both open submissions.
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentorA.UserId, new(adr2.SubmissionId, "Kompletne", "Drobne", "Kiedy wydzielić?", ReviewVerdict.Accepted, scoresA), default)).ShouldSucceed();
        (await App.Get<IReviewService>().SubmitFeedbackAsync(mentorA.UserId, new(practical.SubmissionId, "Testy", "README", "Retencja kluczy?", ReviewVerdict.Accepted, scoresA), default)).ShouldSucceed();
        (await App.Get<IReflectionService>().SaveReflectionAsync(mentee.UserId, new(adr2.SubmissionId, 5, "Rozumiem kompromisy"), default)).ShouldSucceed();

        // 11. Roadmap progress is updated.
        var progress = (await App.Get<IProgressService>().GetRoadmapProgressAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        progress.CompletedTopics.ShouldBe(3);
        progress.PercentComplete.ShouldBe(100);
        progress.Topics.ShouldAllBe(t => t.Status == TopicProgressStatus.Completed);
        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, aDecision, default)).ShouldSucceed();
        history.Attempts.Count.ShouldBe(2);
        history.Attempts.Single(a => a.AttemptNumber == 1).Feedback!.Verdict.ShouldBe(ReviewVerdict.NeedsRevision);

        // 12. Mentor B cannot read any resource from this process.
        var b = mentorB.UserId;
        var attachmentId = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, aPractical, default)).ShouldSucceed().Attempts.Single().Attachment!.AttachmentId;
        (await App.Get<IMentorshipService>().ListForMentorAsync(b, default)).ShouldSucceed().ShouldBeEmpty();
        (await App.Get<IMentorshipService>().GetForMentorAsync(b, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await App.Get<IInvitationService>().ListAsync(b, default)).ShouldSucceed().ShouldBeEmpty();
        (await App.Get<ITopicService>().ListAsync(b, true, default)).ShouldSucceed().ShouldBeEmpty();
        foreach (var topic in new[] { topicQuiz, topicDecisions, topicPractical })
            (await App.Get<ITopicService>().GetAsync(b, topic, default)).ShouldBeNotFound();
        foreach (var exercise in new[] { quizExercise, ai.ExerciseId, practicalExercise })
            (await App.Get<IExerciseAuthoringService>().GetMentorExerciseAsync(b, exercise, default)).ShouldBeNotFound();
        (await App.Get<IExerciseAuthoringService>().ListAsync(b, new(null, null, null), default)).ShouldSucceed().ShouldBeEmpty();
        (await App.Get<IRoadmapService>().GetForMentorAsync(b, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await App.Get<IRoadmapService>().GetForMenteeAsync(b, mentee.MentorshipId, default)).ShouldBeNotFound();
        foreach (var assignment in new[] { aQuiz, aDecision, aPractical })
        {
            (await App.Get<ISubmissionService>().GetHistoryAsync(b, assignment, default)).ShouldBeNotFound();
            (await App.Get<IExerciseAuthoringService>().GetMenteeExerciseAsync(b, assignment, default)).ShouldBeNotFound();
        }
        foreach (var submission in new[] { adr1.SubmissionId, adr2.SubmissionId, practical.SubmissionId })
        {
            (await App.Get<IReviewService>().GetReviewDetailAsync(b, submission, default)).ShouldBeNotFound();
            (await App.Get<IReviewService>().SubmitFeedbackAsync(b, new(submission, "x", "x", "x", ReviewVerdict.Accepted), default)).ShouldBeNotFound();
        }
        (await App.Get<ISubmissionService>().OpenAttachmentAsync(b, attachmentId, default)).ShouldBeNotFound();
        (await App.Get<IReviewService>().GetPendingAsync(b, default)).ShouldSucceed().ShouldBeEmpty();
        (await App.Get<IProgressService>().GetRoadmapProgressAsync(b, mentee.MentorshipId, default)).ShouldBeNotFound();
        var dashboardB = (await App.Get<IProgressService>().GetMentorDashboardAsync(b, default)).ShouldSucceed();
        dashboardB.Mentees.ShouldBeEmpty();
        dashboardB.RecentActivity.ShouldBeEmpty();
        (await App.Get<IAiExerciseDraftService>().GetQuotaAsync(b, default)).ShouldSucceed().Used.ShouldBe(0);
    }
}
