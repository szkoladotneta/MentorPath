using MentorPath.Application.Catalog.Contracts;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;

namespace MentorPath.IntegrationTests.Reviews;

public abstract class ReviewFeatureTestBase(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => EnabledSettings;
    protected static Dictionary<string, string?> EnabledSettings => new()
    {
        ["Mcp:Reviews:Enabled"] = "true",
        ["Mcp:Reviews:ProviderNotice"] = "Dane trafiają do dostawcy skonfigurowanego przez mentora.",
    };

    protected sealed record ReviewCase(MentorRef Mentor, MenteeRef Mentee, AssignmentRef Assignment,
        Guid SubmissionId, IReadOnlyList<RubricCriterionDto> Criteria);

    protected async Task<ReviewCase> PendingAsync(bool consent = true)
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var criteria = (await App.Get<IRubricService>().ListCriteriaAsync(mentor.UserId, true, default)).ShouldSucceed();
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision,
            criterionIds: criteria.Take(2).Select(c => c.CriterionId).ToList());
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        if (consent) await GrantAsync(mentee);
        return new(mentor, mentee, assignment, submission.SubmissionId, criteria);
    }

    protected sealed record AttachmentCase(ReviewCase Review, AttachmentInfo Attachment, byte[] Bytes);

    protected async Task<AttachmentCase> PracticalAsync(bool consent = true, int size = 100_000)
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var criteria = (await App.Get<IRubricService>().ListCriteriaAsync(mentor.UserId, true, default)).ShouldSucceed();
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.PracticalTask,
            criterionIds: criteria.Take(2).Select(c => c.CriterionId).ToList());
        var bytes = Enumerable.Range(0, size).Select(i => (byte)('A' + i % 26)).ToArray();
        using var content = new MemoryStream(bytes, writable: false);
        var submission = (await App.Get<ISubmissionService>().SubmitPracticalAsync(mentee.UserId,
            new(assignment.AssignmentId, Guid.NewGuid(), "Uzasadnienie", 3, "https://example.test/solution",
                new("solution.txt", "text/plain", bytes.Length, content)), default)).ShouldSucceed();
        if (consent) await GrantAsync(mentee);
        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, assignment.AssignmentId, default)).ShouldSucceed();
        return new(new(mentor, mentee, assignment, submission.SubmissionId, criteria), history.Attempts.Single().Attachment!, bytes);
    }

    protected async Task<MentorshipReviewConsentDto> GrantAsync(MenteeRef mentee)
    {
        var service = App.Get<IMentorshipReviewConsentService>();
        var existing = (await service.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        return (await service.GrantAsync(mentee.UserId, new(mentee.MentorshipId,
            App.Get<IOptions<McpReviewOptions>>().Value.DisclosureVersion, existing.Version), default)).ShouldSucceed();
    }

    protected static ReviewDraftContent Complete(ReviewCase item, ReviewVerdict verdict = ReviewVerdict.Accepted) =>
        new("  Mocne strony  ", " Do poprawy ", " Pytanie ", verdict,
            item.Criteria.Take(2).Select(c => new CriterionScoreInput(c.CriterionId, 4, " Komentarz ")).ToList());

    protected async Task<ReviewDraftDto> SaveAsync(ReviewCase item, ReviewDraftContent? content = null, long? version = null) =>
        (await App.Get<IReviewDraftService>().SaveAsync(item.Mentor.UserId,
            new(item.SubmissionId, content ?? Complete(item), version), default)).ShouldSucceed();
}
