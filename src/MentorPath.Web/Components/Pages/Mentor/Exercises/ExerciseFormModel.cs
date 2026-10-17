using MentorPath.Application.Catalog.Contracts;

namespace MentorPath.Web.Components.Pages.Mentor.Exercises;

public sealed class ExerciseFormModel
{
    public string Title { get; set; } = "";
    public string ScenarioMarkdown { get; set; } = "";
    public string? InstructionsMarkdown { get; set; }
    public string? ExpectedOutcomeMarkdown { get; set; }
    public string? RubricMarkdown { get; set; }
    public string? DiscussionQuestionsMarkdown { get; set; }
    public string? ExplanationMarkdown { get; set; }
    public string? MentorGuideMarkdown { get; set; }
    public int? EstimatedMinutes { get; set; }
    public List<OptionModel> QuizOptions { get; set; } = [];

    public sealed class OptionModel
    {
        public string Text { get; set; } = "";
        public bool IsCorrect { get; set; }
        public string? ExplanationMarkdown { get; set; }
    }

    public ExerciseContent ToContent() => new(Title, ScenarioMarkdown, InstructionsMarkdown, ExpectedOutcomeMarkdown, RubricMarkdown,
        DiscussionQuestionsMarkdown, ExplanationMarkdown, MentorGuideMarkdown, EstimatedMinutes,
        QuizOptions.Select(o => new QuizOptionInput(o.Text, o.IsCorrect, o.ExplanationMarkdown)).ToList());

    public static ExerciseFormModel From(ExerciseContent c) => new()
    {
        Title = c.Title, ScenarioMarkdown = c.ScenarioMarkdown, InstructionsMarkdown = c.InstructionsMarkdown,
        ExpectedOutcomeMarkdown = c.ExpectedOutcomeMarkdown, RubricMarkdown = c.RubricMarkdown,
        DiscussionQuestionsMarkdown = c.DiscussionQuestionsMarkdown, ExplanationMarkdown = c.ExplanationMarkdown,
        MentorGuideMarkdown = c.MentorGuideMarkdown, EstimatedMinutes = c.EstimatedMinutes,
        QuizOptions = c.QuizOptions.Select(o => new OptionModel { Text = o.Text, IsCorrect = o.IsCorrect, ExplanationMarkdown = o.ExplanationMarkdown }).ToList(),
    };
}
