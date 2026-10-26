using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Mentorship> Mentorships => Set<Mentorship>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<TopicResource> TopicResources => Set<TopicResource>();
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();
    public DbSet<Roadmap> Roadmaps => Set<Roadmap>();
    public DbSet<RoadmapItem> RoadmapItems => Set<RoadmapItem>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionAttachment> SubmissionAttachments => Set<SubmissionAttachment>();
    public DbSet<MentorFeedback> MentorFeedback => Set<MentorFeedback>();
    public DbSet<RubricCriterion> RubricCriteria => Set<RubricCriterion>();
    public DbSet<ExerciseCriterion> ExerciseCriteria => Set<ExerciseCriterion>();
    public DbSet<FeedbackCriterionScore> FeedbackCriterionScores => Set<FeedbackCriterionScore>();
    public DbSet<SubmissionReflection> SubmissionReflections => Set<SubmissionReflection>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<AiGenerationRequest> AiGenerationRequests => Set<AiGenerationRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<ExerciseType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ExerciseStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ExerciseSource>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<RoadmapStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<SubmissionStatus>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<MentorshipStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<AiRequestStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ReviewVerdict>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<Difficulty>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<ExperienceLevel>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        });
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Domain ids are always assigned by the application (Guid v7), so EF must treat new graphs as Added.
        foreach (var entity in builder.Model.GetEntityTypes().Where(t => t.ClrType.Namespace == typeof(Workspace).Namespace))
        {
            entity.FindProperty("Id")?.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }
}
