using System.ComponentModel;
using ModelContextProtocol.Server;

namespace MentorPath.Web.Mcp.Prompts;

/// <summary>Server-owned workflows. Every client gets the same procedure without hardcoding it in a chat (D-64).</summary>
[McpServerPromptType]
public sealed class MentorPathPrompts
{
    [McpServerPrompt(Name = "plan_roadmap")]
    [Description("Plan a personalised roadmap for one mentee from the mentor's catalog and create it as a Draft for the mentor to review.")]
    public static string PlanRoadmap(
        [Description("The mentorshipId from list_mentees")] string mentorshipId,
        [Description("How many topics the roadmap should have (default 4)")] string? topicCount = null) =>
        $"""
         You are helping a mentor plan a roadmap for the mentee with mentorshipId {mentorshipId}.

         1. Read the mentee profile (tool get_mentee_profile or resource mentorpath://mentees/{mentorshipId}/profile): goal, level, challenges, weekly hours, mentor notes.
         2. Check get_roadmap for this mentorship. If a roadmap already exists, propose additions or reordering instead of creating a new one.
         3. Read the catalog (resource mentorpath://catalog or list_topics + get_topic). Choose {topicCount ?? "4"} topics that serve the goal and the challenges, ordered from prerequisites to advanced, and matched to the mentee's level.
         4. For each topic pick one or two Published exercises; prefer one ArchitecturalDecision per topic. If a topic has no published exercise, say so and propose a draft with create_exercise_draft, but do not assign drafts.
         5. Present the plan as a short table (order, topic, exercises, why) and wait for the mentor's confirmation.
         6. After confirmation: create_roadmap (title + objective in Markdown), then add_roadmap_topic for each topic in order (a one-sentence mentor note per topic), then assign_exercise for each chosen exercise.
         7. Finish with a summary of what was created and remind the mentor that the roadmap is a Draft to publish in the UI. Never publish anything yourself.
         """;

    [McpServerPrompt(Name = "draft_exercise")]
    [Description("Write one exercise draft in MentorPath's structure for a given topic and type, then save it with create_exercise_draft.")]
    public static string DraftExercise(
        [Description("The topicId from list_topics")] string topicId,
        [Description("QuizSingleChoice, ArchitecturalDecision or PracticalTask")] string type) =>
        $"""
         Draft one exercise of type {type} for topicId {topicId} in the mentor's workspace.

         1. Read the topic with get_topic: description, learning objectives, existing exercises (avoid duplicating them).
         2. Write the exercise in Polish, in Markdown, for a backend .NET developer. Keep it grounded in a realistic scenario (team size, traffic, constraints, deadline).
            - QuizSingleChoice: scenario, 3-4 options with exactly one correct, a short explanation per option, an explanationMarkdown for the whole question, estimatedMinutes 5-15.
            - ArchitecturalDecision: scenario; instructionsMarkdown with explicit constraints and requirements; expectedOutcomeMarkdown saying the answer has decision, rationale, alternatives, trade-offs, risks and conditions for reconsideration; rubricMarkdown with 3-5 criteria; estimatedMinutes 45-90.
            - PracticalTask: task description in scenarioMarkdown; requirements in instructionsMarkdown; expected artifacts (repository, tests, short ADR) in expectedOutcomeMarkdown; rubricMarkdown; discussionQuestionsMarkdown with 2-3 questions about implementation decisions; estimatedMinutes 120-300.
         3. Always fill mentorGuideMarkdown with what a strong answer looks like and the usual mistakes.
         4. Show the draft to the mentor, apply their corrections, then save it with create_exercise_draft. It stays a Draft; the mentor publishes it in the UI.
         """;
}
