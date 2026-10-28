using MentorPath.Application.Common;

namespace MentorPath.Application.Feedback.Contracts;

/// <summary>
/// Write-only product feedback from mentors and mentees (N03, D-56). Feedback is global; no workspace is stored.
/// Authorization is a workspace relationship (workspace owner, or mentee of a mentorship in any status), never a role alone.
/// Error codes: <c>feedback.not_found</c> (NotFound: no workspace relationship, including unknown user and operator-only accounts),
/// <c>feedback.invalid</c> (Validation: empty or too long message, page path too long).
/// </summary>
public interface IProductFeedbackService
{
    Task<Result> SubmitAsync(Guid actorUserId, SubmitProductFeedbackRequest request, CancellationToken ct);
}

/// <summary><paramref name="PagePath"/> is optional; any query string or fragment is dropped on the server.</summary>
public sealed record SubmitProductFeedbackRequest(string Message, string? PagePath);
