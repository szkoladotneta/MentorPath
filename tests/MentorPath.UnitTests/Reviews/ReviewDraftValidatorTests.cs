using MentorPath.Application.Common;
using MentorPath.Application.Reviews;
using MentorPath.Application.Reviews.Contracts;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.Reviews;

public sealed class ReviewDraftValidatorTests
{
    private readonly RubricCriterion _criterion = new() { Id = Guid.NewGuid(), Name = "Decyzja", IsActive = true };
    private static ReviewDraftContent Empty => new(null, null, null, null, []);

    [Fact]
    public void Partial_drafts_allow_absent_fields_verdict_and_missing_scores()
    {
        ReviewDraftValidator.Validate(Empty, [_criterion]).HasErrors.ShouldBeFalse();
        ReviewDraftValidator.Validate(Empty with { StrengthsMarkdown = "  " }, [_criterion]).HasErrors.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Draft_scores_use_the_full_review_range(int score)
    {
        var result = ReviewDraftValidator.Validate(Empty with { Scores = [new(_criterion.Id, score, "")] }, [_criterion]);
        result.ToDictionary().ShouldContainKey("Scores[0].Score");
    }

    [Fact]
    public void Duplicate_and_unselected_scores_are_rejected()
    {
        ReviewDraftValidator.Validate(Empty with { Scores = [new(_criterion.Id, 3, ""), new(_criterion.Id, 4, "")] }, [_criterion])
            .ToDictionary().ShouldContainKey("Scores");
        ReviewDraftValidator.Validate(Empty with { Scores = [new(Guid.NewGuid(), 3, "")] }, [_criterion])
            .ToDictionary().ShouldContainKey("Scores");
    }

    [Fact]
    public void Text_fields_and_comments_have_the_same_limit_as_published_feedback()
    {
        var longText = new string('x', InputLimits.Markdown + 1);
        var result = ReviewDraftValidator.Validate(new(longText, longText, longText, null,
            [new(_criterion.Id, 5, longText)]), [_criterion]).ToDictionary();
        result.Keys.Order().ShouldBe(new[]
            { "StrengthsMarkdown", "ImprovementsMarkdown", "ChallengeQuestionMarkdown", "Scores[0].CommentMarkdown" }.Order());
        ReviewDraftValidator.Validate(Empty with { StrengthsMarkdown = new string('x', InputLimits.Markdown) }, [_criterion])
            .HasErrors.ShouldBeFalse();
    }

    [Fact]
    public void Undefined_verdict_is_not_a_valid_partial_value() =>
        ReviewDraftValidator.Validate(Empty with { Verdict = (ReviewVerdict)987 }, [_criterion])
            .ToDictionary().ShouldContainKey("Verdict");
}
