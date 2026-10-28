using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Isolation;

public sealed class ForeignActorIsolationTests(PostgresFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task Submissions_are_not_found_for_foreign_mentee_and_foreign_mentor()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var otherMentee = await Data.MenteeAsync(a);
        var quiz = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.QuizSingleChoice);
        var practical = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.PracticalTask);
        var decision = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        var svc = App.Get<ISubmissionService>();
        var unknownUserId = Guid.NewGuid();

        foreach (var actor in new[] { otherMentee.UserId, b.UserId, a.UserId, unknownUserId })
        {
            (await svc.SubmitQuizAsync(actor, new(quiz.AssignmentId, Guid.NewGuid(), Guid.NewGuid(), "x", 3), default)).ShouldBeNotFound();
            (await svc.SubmitPracticalAsync(actor, new(practical.AssignmentId, Guid.NewGuid(), "x", 3, "https://example.com", null), default)).ShouldBeNotFound();
            (await svc.SubmitDecisionAsync(actor, new(decision.AssignmentId, Guid.NewGuid(), new("D", "R", "A", "T", "Ri", "C"), 3), default)).ShouldBeNotFound();
        }

        foreach (var actor in new[] { otherMentee.UserId, b.UserId, unknownUserId })
            (await svc.GetHistoryAsync(actor, decision.AssignmentId, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Roadmap_item_operations_are_not_found_for_foreign_mentor()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.QuizSingleChoice);
        var svc = App.Get<IRoadmapService>();

        (await svc.UpdateTopicNoteAsync(b.UserId, asg.RoadmapItemId, "x", default)).ShouldBeNotFound();
        (await svc.ReorderAssignmentsAsync(b.UserId, asg.RoadmapItemId, [asg.AssignmentId], default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Mentorship_operations_are_not_found_for_foreign_actors()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var otherMentee = await Data.MenteeAsync(a);
        var svc = App.Get<IMentorshipService>();

        (await svc.GetOnboardingAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await svc.GetOnboardingAsync(otherMentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await svc.SaveOnboardingAsync(otherMentee.UserId, mentee.MentorshipId, new("Cel", ExperienceLevel.Mid, "Wyzwania", 5), default)).ShouldBeNotFound();
        (await svc.SaveOnboardingAsync(b.UserId, mentee.MentorshipId, new("Cel", ExperienceLevel.Mid, "Wyzwania", 5), default)).ShouldBeNotFound();
        (await svc.ChangeStatusAsync(b.UserId, mentee.MentorshipId, MentorshipStatus.Paused, default)).ShouldBeNotFound();
        (await svc.ChangeStatusAsync(mentee.UserId, mentee.MentorshipId, MentorshipStatus.Paused, default)).ShouldBeNotFound();
    }

    [Fact]
    public async Task Accepting_an_invitation_as_unknown_user_is_not_found()
    {
        var a = await Data.MentorAsync();
        var invite = (await App.Get<IInvitationService>().CreateAsync(a.UserId, new("someone@test.local"), default)).ShouldSucceed();

        (await App.Get<IInvitationService>().AcceptAsExistingUserAsync(Guid.NewGuid(), TestDataBuilder.TokenOf(invite.AcceptUrl), default)).ShouldBeNotFound();
    }
}
