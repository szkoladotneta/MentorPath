using MentorPath.Application.Reviews.Contracts;

namespace MentorPath.UnitTests.Reviews;

public sealed class McpReviewContractTests
{
    [Fact]
    public void Attempt_context_has_explicit_learning_history_fields_without_UI_actions_or_private_identity()
    {
        string[] expected =
        [
            "SubmissionId", "AttemptNumber", "Status", "SubmittedAtUtc", "ConfidenceBefore",
            "SelectedOptionText", "DecisionAnswer", "AnswerMarkdown", "ExternalUrl",
            "Attachment", "Feedback", "Reflection",
        ];
        var actual = typeof(McpSubmissionAttemptDto).GetProperties().Select(p => p.Name).Order().ToArray();

        actual.ShouldBe(expected.Order().ToArray());
        typeof(McpReviewContextDto).GetProperty(nameof(McpReviewContextDto.Submission))!
            .PropertyType.ShouldBe(typeof(McpSubmissionAttemptDto));
        typeof(McpSubmissionHistoryPage).GetProperty(nameof(McpSubmissionHistoryPage.Attempts))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<McpSubmissionAttemptDto>));
    }

    [Fact]
    public void MCP_draft_contract_excludes_publication_while_human_contract_requires_explicit_version()
    {
        var mcpMethods = typeof(IMcpReviewDraftService).GetMethods().Select(m => m.Name).Order().ToArray();
        mcpMethods.ShouldBe(["GetAsync", "SaveAsync"]);
        var publish = typeof(IReviewDraftService).GetMethod(nameof(IReviewDraftService.PublishAsync));
        publish.ShouldNotBeNull();
        publish.GetParameters().Select(p => p.Name).ToArray()
            .ShouldBe(["actorUserId", "submissionId", "expectedVersion", "ct"]);
        publish.GetParameters()[2].ParameterType.ShouldBe(typeof(long));
    }
}
