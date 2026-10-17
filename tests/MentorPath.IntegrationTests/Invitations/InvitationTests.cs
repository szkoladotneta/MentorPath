using MentorPath.Application.Common;
using MentorPath.Application.Invitations.Contracts;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Invitations;

public sealed class InvitationTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IInvitationService Invitations => App.Get<IInvitationService>();

    [Fact]
    public async Task Only_token_hash_is_stored_and_link_contains_raw_token()
    {
        var mentor = await Data.MentorAsync();
        var invite = (await Invitations.CreateAsync(mentor.UserId, new("someone@test.local"), default)).ShouldSucceed();
        var token = TestDataBuilder.TokenOf(invite.AcceptUrl);

        var stored = await App.Db(ctx => ctx.Invitations.SingleAsync(i => i.Id == invite.InvitationId));
        stored.TokenHash.ShouldNotBe(token);
        stored.TokenHash.Length.ShouldBe(64);
        stored.ExpiresAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime.AddDays(7));
    }

    [Fact]
    public async Task Token_is_single_use()
    {
        var mentor = await Data.MentorAsync();
        var invite = (await Invitations.CreateAsync(mentor.UserId, new("single@test.local"), default)).ShouldSucceed();
        var token = TestDataBuilder.TokenOf(invite.AcceptUrl);
        (await Invitations.AcceptWithNewAccountAsync(new(token, "Single", TestDataBuilder.Password), default)).ShouldSucceed();

        (await App.Get<IInvitationService>().AcceptWithNewAccountAsync(new(token, "Again", TestDataBuilder.Password), default))
            .ShouldFail(ErrorKind.Validation, "invitation.invalid_or_used");
        (await App.Get<IInvitationService>().PreviewAsync(token, default)).ShouldFail(ErrorKind.Validation, "invitation.invalid_or_used");
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var mentor = await Data.MentorAsync();
        var invite = (await Invitations.CreateAsync(mentor.UserId, new("late@test.local"), default)).ShouldSucceed();
        App.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        (await App.Get<IInvitationService>().AcceptWithNewAccountAsync(new(TestDataBuilder.TokenOf(invite.AcceptUrl), "Late", TestDataBuilder.Password), default))
            .ShouldFail(ErrorKind.Validation, "invitation.invalid_or_used");
    }

    [Fact]
    public async Task Existing_account_with_different_email_cannot_accept()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var someone = await Data.MenteeAsync(mentorA);
        var invite = (await Invitations.CreateAsync(mentorB.UserId, new("other-person@test.local"), default)).ShouldSucceed();

        (await App.Get<IInvitationService>().AcceptAsExistingUserAsync(someone.UserId, TestDataBuilder.TokenOf(invite.AcceptUrl), default))
            .ShouldFail(ErrorKind.Validation, "invitation.email_mismatch");
    }

    [Fact]
    public async Task New_account_cannot_be_created_when_email_already_registered()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var existing = await Data.MenteeAsync(mentorA);
        var invite = (await Invitations.CreateAsync(mentorB.UserId, new(existing.Email), default)).ShouldSucceed();

        (await App.Get<IInvitationService>().AcceptWithNewAccountAsync(new(TestDataBuilder.TokenOf(invite.AcceptUrl), "Dup", TestDataBuilder.Password), default))
            .ShouldFail(ErrorKind.Conflict, "invitation.account_exists");
    }

    [Fact]
    public async Task Concurrent_acceptance_of_same_token_succeeds_once()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentorA);
        var invite = (await Invitations.CreateAsync(mentorB.UserId, new(mentee.Email), default)).ShouldSucceed();
        var token = TestDataBuilder.TokenOf(invite.AcceptUrl);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            App.Get<IInvitationService>().AcceptAsExistingUserAsync(mentee.UserId, token, default)));
        results.Count(r => r.IsSuccess).ShouldBe(1);
        (await App.Db(ctx => ctx.Mentorships.CountAsync(m => m.WorkspaceId == mentorB.WorkspaceId && m.MenteeUserId == mentee.UserId))).ShouldBe(1);
    }

    [Fact]
    public async Task Mentor_cannot_invite_self_or_active_member()
    {
        var mentor = await Data.MentorAsync();
        (await Invitations.CreateAsync(mentor.UserId, new(mentor.Email), default)).ShouldFail(ErrorKind.Validation, "invitation.self_invite");
        var mentee = await Data.MenteeAsync(mentor);
        (await App.Get<IInvitationService>().CreateAsync(mentor.UserId, new(mentee.Email), default)).ShouldFail(ErrorKind.Conflict, "invitation.already_member");
    }

    [Fact]
    public async Task New_invitation_expires_previous_pending_one()
    {
        var mentor = await Data.MentorAsync();
        var first = (await Invitations.CreateAsync(mentor.UserId, new("twice@test.local"), default)).ShouldSucceed();
        (await App.Get<IInvitationService>().CreateAsync(mentor.UserId, new("twice@test.local"), default)).ShouldSucceed();

        (await App.Get<IInvitationService>().PreviewAsync(TestDataBuilder.TokenOf(first.AcceptUrl), default)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Acceptance_creates_active_mentorship_and_mentee_role_and_onboarding_is_private_to_mentee()
    {
        var mentor = await Data.MentorAsync();
        var otherMentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var service = App.Get<IMentorshipService>();

        var list = (await service.ListForMenteeAsync(mentee.UserId, default)).ShouldSucceed();
        list.Single().Status.ShouldBe(MentorshipStatus.Active);
        (await service.SaveOnboardingAsync(mentee.UserId, mentee.MentorshipId, new("Cel", ExperienceLevel.Mid, "Wyzwania", 5), default)).ShouldSucceed();
        (await service.UpdatePrivateNotesAsync(mentor.UserId, mentee.MentorshipId, "tajne", default)).ShouldSucceed();

        var detail = (await service.GetForMentorAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        detail.Goal.ShouldBe("Cel");
        detail.MentorPrivateNotesMarkdown.ShouldBe("tajne");

        typeof(OnboardingDto).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Notes"));
        (await service.GetForMentorAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await service.GetForMentorAsync(otherMentor.UserId, mentee.MentorshipId, default)).ShouldBeNotFound();
        (await service.UpdatePrivateNotesAsync(otherMentor.UserId, mentee.MentorshipId, "x", default)).ShouldBeNotFound();
        (await service.ListForMentorAsync(otherMentor.UserId, default)).ShouldSucceed().ShouldBeEmpty();
        (await App.Get<IInvitationService>().ListAsync(otherMentor.UserId, default)).ShouldSucceed().ShouldBeEmpty();
    }
}
