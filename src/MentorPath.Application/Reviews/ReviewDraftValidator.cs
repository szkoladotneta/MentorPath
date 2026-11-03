using MentorPath.Application.Common;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Entities;

namespace MentorPath.Application.Reviews;

public static class ReviewDraftValidator
{
    public static FieldErrors Validate(ReviewDraftContent content, IReadOnlyList<RubricCriterion> selectedActiveCriteria)
    {
        var errors = new FieldErrors();
        errors.Optional(nameof(content.StrengthsMarkdown), content.StrengthsMarkdown, InputLimits.Markdown, "Mocne strony");
        errors.Optional(nameof(content.ImprovementsMarkdown), content.ImprovementsMarkdown, InputLimits.Markdown, "Do poprawy");
        errors.Optional(nameof(content.ChallengeQuestionMarkdown), content.ChallengeQuestionMarkdown, InputLimits.Markdown, "Pytanie pogłębiające");
        if (content.Verdict is not null && !Enum.IsDefined(content.Verdict.Value))
            errors.Add(nameof(content.Verdict), "Wybierz werdykt.");
        ReviewService.ValidateScores(errors, content.Scores, selectedActiveCriteria, requireAll: false);
        return errors;
    }
}
