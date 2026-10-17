using MentorPath.Application.Catalog.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.Web.Components.Pages.Mentor.Catalog;

public sealed class TopicFormModel
{
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Category { get; set; } = "";
    public Difficulty Difficulty { get; set; } = Difficulty.Intermediate;
    public string DescriptionMarkdown { get; set; } = "";
    public string LearningObjectivesMarkdown { get; set; } = "";

    public UpsertTopicRequest ToRequest() => new(Title, Slug, Category, Difficulty, DescriptionMarkdown, LearningObjectivesMarkdown);

    public static TopicFormModel From(TopicDetail t) => new()
    {
        Title = t.Title, Slug = t.Slug, Category = t.Category, Difficulty = t.Difficulty,
        DescriptionMarkdown = t.DescriptionMarkdown, LearningObjectivesMarkdown = t.LearningObjectivesMarkdown,
    };
}
