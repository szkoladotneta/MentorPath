namespace MentorPath.Application.Feedback.Contracts;

/// <summary>
/// Operator-only read access to product feedback, executed from the CLI (`list-feedback`). There is no logged-in actor,
/// so this method is exempt from the `actorUserId` convention and only CLI commands may use the service
/// (<c>ConventionTests.Operator_services_are_used_only_by_the_CLI</c>). Users can never read feedback back.
/// </summary>
public interface IProductFeedbackOperatorService
{
    /// <summary>Newest first. <paramref name="limit"/> is clamped to 1..500.</summary>
    Task<IReadOnlyList<ProductFeedbackListItem>> ListAsync(int limit, CancellationToken ct);
}

/// <summary><paramref name="Message"/> is empty once the author has been anonymized.</summary>
public sealed record ProductFeedbackListItem(Guid Id, Guid AuthorUserId, string Message, string? PagePath, DateTime CreatedAtUtc);
