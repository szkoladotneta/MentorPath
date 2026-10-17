using MentorPath.Application.Common.Access;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Roadmaps.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Access;

public sealed class WorkspaceAccessServiceTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private IWorkspaceAccessService Access => App.Get<IWorkspaceAccessService>();

    [Fact]
    public async Task Mentor_owns_only_own_workspace()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        (await Access.GetOwnedWorkspaceIdAsync(a.UserId, default)).ShouldBe(a.WorkspaceId);
        (await Access.CanManageWorkspaceAsync(a.UserId, a.WorkspaceId, default)).ShouldBeTrue();
        (await Access.CanManageWorkspaceAsync(b.UserId, a.WorkspaceId, default)).ShouldBeFalse();
    }

    [Fact]
    public async Task Mentorship_access_matrix()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var other = await Data.MenteeAsync(a);

        (await Access.GetMentorshipAccessAsync(a.UserId, mentee.MentorshipId, default))!.Role.ShouldBe(AccessRole.WorkspaceOwner);
        (await Access.GetMentorshipAccessAsync(mentee.UserId, mentee.MentorshipId, default))!.Role.ShouldBe(AccessRole.Mentee);
        (await Access.GetMentorshipAccessAsync(b.UserId, mentee.MentorshipId, default)).ShouldBeNull();
        (await Access.GetMentorshipAccessAsync(other.UserId, mentee.MentorshipId, default)).ShouldBeNull();
        (await Access.GetMentorshipAccessAsync(a.UserId, Guid.NewGuid(), default)).ShouldBeNull();
    }

    [Fact]
    public async Task Paused_mentorship_is_read_only_and_closed_mentorship_hides_from_mentee()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var mentorships = App.Get<IMentorshipService>();

        (await mentorships.ChangeStatusAsync(a.UserId, mentee.MentorshipId, MentorshipStatus.Paused, default)).ShouldSucceed();
        var paused = await Access.GetMentorshipAccessAsync(mentee.UserId, mentee.MentorshipId, default);
        paused.ShouldNotBeNull();
        paused.MenteeCanWrite.ShouldBeFalse();

        (await mentorships.ChangeStatusAsync(a.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default)).ShouldSucceed();
        (await Access.GetMentorshipAccessAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldBeNull();
        (await Access.GetMentorshipAccessAsync(a.UserId, mentee.MentorshipId, default)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Mentee_cannot_access_assignment_of_unpublished_roadmap()
    {
        var a = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision, publish: false);

        (await Access.GetAssignmentAccessAsync(mentee.UserId, asg.AssignmentId, default)).ShouldBeNull();
        (await Access.GetAssignmentAccessAsync(a.UserId, asg.AssignmentId, default)).ShouldNotBeNull();

        (await App.Get<IRoadmapService>().PublishAsync(a.UserId, asg.RoadmapId, default)).ShouldSucceed();
        (await Access.GetAssignmentAccessAsync(mentee.UserId, asg.AssignmentId, default))!.Role.ShouldBe(AccessRole.Mentee);
    }

    [Fact]
    public async Task Submission_access_is_limited_to_author_and_workspace_owner()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(a);
        var otherMentee = await Data.MenteeAsync(a);
        var asg = await Data.PublishedAssignmentAsync(a, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, asg.AssignmentId);

        (await Access.CanAccessSubmissionAsync(mentee.UserId, submission.SubmissionId, default)).ShouldBeTrue();
        (await Access.CanAccessSubmissionAsync(a.UserId, submission.SubmissionId, default)).ShouldBeTrue();
        (await Access.CanAccessSubmissionAsync(b.UserId, submission.SubmissionId, default)).ShouldBeFalse();
        (await Access.CanAccessSubmissionAsync(otherMentee.UserId, submission.SubmissionId, default)).ShouldBeFalse();
    }

    [Fact]
    public async Task Mentee_in_two_workspaces_sees_two_separate_mentorships()
    {
        var a = await Data.MentorAsync();
        var b = await Data.MentorAsync();
        var inA = await Data.MenteeAsync(a);
        var inB = await Data.MenteeAsync(b, inA.Email, inA.UserId);

        inB.UserId.ShouldBe(inA.UserId);
        inB.MentorshipId.ShouldNotBe(inA.MentorshipId);
        var list = (await App.Get<IMentorshipService>().ListForMenteeAsync(inA.UserId, default)).ShouldSucceed();
        list.Select(m => m.MentorshipId).ShouldBe([inA.MentorshipId, inB.MentorshipId], ignoreOrder: true);

        // Mentor B still cannot see the relation in workspace A.
        (await Access.GetMentorshipAccessAsync(b.UserId, inA.MentorshipId, default)).ShouldBeNull();
    }
}
