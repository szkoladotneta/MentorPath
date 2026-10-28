using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MentorPath.Data.Configurations;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> e)
    {
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.OwnerUserId).IsUnique();
        e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MentorshipConfiguration : IEntityTypeConfiguration<Mentorship>
{
    public void Configure(EntityTypeBuilder<Mentorship> e)
    {
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.WorkspaceId, x.MenteeUserId }).IsUnique();
        e.HasIndex(x => x.MenteeUserId);
        e.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Mentee).WithMany().HasForeignKey(x => x.MenteeUserId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_Mentorships_WeeklyHours", "\"WeeklyHours\" IS NULL OR (\"WeeklyHours\" BETWEEN 1 AND 80)"));
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> e)
    {
        e.Property(x => x.NormalizedEmail).HasMaxLength(320).IsRequired();
        e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => new { x.WorkspaceId, x.NormalizedEmail });
        e.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> e)
    {
        e.Property(x => x.Slug).HasMaxLength(150).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Category).HasMaxLength(80).IsRequired();
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.WorkspaceId, x.Slug }).IsUnique();
        e.HasIndex(x => new { x.WorkspaceId, x.ParentTopicId });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Topic>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ParentTopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_Topics_NotOwnParent", "\"ParentTopicId\" IS NULL OR \"ParentTopicId\" <> \"Id\""));
    }
}

internal sealed class CompetencyPrerequisiteConfiguration : IEntityTypeConfiguration<CompetencyPrerequisite>
{
    public void Configure(EntityTypeBuilder<CompetencyPrerequisite> e)
    {
        e.HasKey(x => new { x.WorkspaceId, x.TopicId, x.PrerequisiteTopicId });
        e.HasIndex(x => x.WorkspaceId);
        e.HasIndex(x => new { x.WorkspaceId, x.PrerequisiteTopicId });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Topic>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.TopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Topic>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.PrerequisiteTopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_CompetencyPrerequisites_NotSelf", "\"TopicId\" <> \"PrerequisiteTopicId\""));
    }
}

internal sealed class TopicResourceConfiguration : IEntityTypeConfiguration<TopicResource>
{
    public void Configure(EntityTypeBuilder<TopicResource> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();
        e.Property(x => x.Description).HasMaxLength(1000);
        e.HasIndex(x => x.WorkspaceId);
        e.HasIndex(x => new { x.WorkspaceId, x.TopicId, x.Position });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Topic).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.TopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_TopicResources_Position", "\"Position\" >= 0"));
    }
}

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.WorkspaceId, x.TopicId, x.Status });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Topic).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.TopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.QuizOptions).WithOne().HasForeignKey(o => o.ExerciseId).OnDelete(DeleteBehavior.Cascade);
        e.ToTable(t => t.HasCheckConstraint("CK_Exercises_EstimatedMinutes", "\"EstimatedMinutes\" IS NULL OR (\"EstimatedMinutes\" BETWEEN 1 AND 600)"));
    }
}

internal sealed class QuizOptionConfiguration : IEntityTypeConfiguration<QuizOption>
{
    public void Configure(EntityTypeBuilder<QuizOption> e)
    {
        e.HasIndex(x => x.ExerciseId).IsUnique().HasFilter("\"IsCorrect\"").HasDatabaseName("IX_QuizOptions_ExerciseId_SingleCorrect");
        e.HasIndex(x => new { x.ExerciseId, x.Position });
    }
}

internal sealed class RoadmapConfiguration : IEntityTypeConfiguration<Roadmap>
{
    public void Configure(EntityTypeBuilder<Roadmap> e)
    {
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => x.MentorshipId).IsUnique();
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Mentorship).WithOne(m => m.Roadmap)
            .HasForeignKey<Roadmap>(x => new { x.WorkspaceId, x.MentorshipId })
            .HasPrincipalKey<Mentorship>(m => new { m.WorkspaceId, m.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Items).WithOne(i => i.Roadmap)
            .HasForeignKey(i => new { i.WorkspaceId, i.RoadmapId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoadmapItemConfiguration : IEntityTypeConfiguration<RoadmapItem>
{
    public void Configure(EntityTypeBuilder<RoadmapItem> e)
    {
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.RoadmapId, x.TopicId }).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Topic).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.TopicId })
            .HasPrincipalKey(t => new { t.WorkspaceId, t.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Assignments).WithOne(a => a.RoadmapItem)
            .HasForeignKey(a => new { a.WorkspaceId, a.RoadmapItemId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> e)
    {
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.RoadmapItemId, x.ExerciseId }).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Exercise).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ExerciseId })
            .HasPrincipalKey(ex => new { ex.WorkspaceId, ex.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Submissions).WithOne(s => s.Assignment)
            .HasForeignKey(s => new { s.WorkspaceId, s.AssignmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> e)
    {
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.Property(x => x.DecisionAnswerJson).HasColumnType("jsonb");
        e.Property(x => x.ExternalUrl).HasMaxLength(2000);
        e.HasIndex(x => new { x.AssignmentId, x.AttemptNumber }).IsUnique();
        e.HasIndex(x => new { x.MenteeUserId, x.ClientRequestId }).IsUnique();
        e.HasIndex(x => x.AssignmentId).IsUnique().HasFilter("\"Status\" = 'PendingReview'").HasDatabaseName("IX_Submissions_AssignmentId_SinglePending");
        e.HasIndex(x => new { x.WorkspaceId, x.Status, x.SubmittedAtUtc });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Mentee).WithMany().HasForeignKey(x => x.MenteeUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.SelectedQuizOption).WithMany().HasForeignKey(x => x.SelectedQuizOptionId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Submissions_ConfidenceBefore", "\"ConfidenceBefore\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Submissions_AttemptNumber", "\"AttemptNumber\" > 0");
        });
    }
}

internal sealed class SubmissionAttachmentConfiguration : IEntityTypeConfiguration<SubmissionAttachment>
{
    public void Configure(EntityTypeBuilder<SubmissionAttachment> e)
    {
        e.Property(x => x.ObjectKey).HasMaxLength(500).IsRequired();
        e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.SubmissionId).IsUnique();
        e.HasIndex(x => x.ObjectKey).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Submission).WithOne(s => s.Attachment)
            .HasForeignKey<SubmissionAttachment>(x => new { x.WorkspaceId, x.SubmissionId })
            .HasPrincipalKey<Submission>(s => new { s.WorkspaceId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MentorFeedbackConfiguration : IEntityTypeConfiguration<MentorFeedback>
{
    public void Configure(EntityTypeBuilder<MentorFeedback> e)
    {
        e.ToTable("MentorFeedback");
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => x.SubmissionId).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.MentorUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Submission).WithOne(s => s.Feedback)
            .HasForeignKey<MentorFeedback>(x => new { x.WorkspaceId, x.SubmissionId })
            .HasPrincipalKey<Submission>(s => new { s.WorkspaceId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductFeedbackConfiguration : IEntityTypeConfiguration<ProductFeedback>
{
    public void Configure(EntityTypeBuilder<ProductFeedback> e)
    {
        e.Property(x => x.MessageText).HasMaxLength(2000).IsRequired();
        e.Property(x => x.PageContext).HasMaxLength(2000);
        e.HasIndex(x => new { x.AuthorUserId, x.CreatedAtUtc });
        e.HasIndex(x => x.CreatedAtUtc);
        e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RubricCriterionConfiguration : IEntityTypeConfiguration<RubricCriterion>
{
    public void Configure(EntityTypeBuilder<RubricCriterion> e)
    {
        e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        e.Property(x => x.Level1Description).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Level2Description).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Level3Description).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Level4Description).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Level5Description).HasMaxLength(1000).IsRequired();
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_RubricCriteria_Position", "\"Position\" >= 0"));
    }
}

internal sealed class ExerciseCriterionConfiguration : IEntityTypeConfiguration<ExerciseCriterion>
{
    public void Configure(EntityTypeBuilder<ExerciseCriterion> e)
    {
        e.HasKey(x => new { x.ExerciseId, x.CriterionId });
        e.HasIndex(x => new { x.WorkspaceId, x.CriterionId });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Exercise).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.ExerciseId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Criterion).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.CriterionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FeedbackCriterionScoreConfiguration : IEntityTypeConfiguration<FeedbackCriterionScore>
{
    public void Configure(EntityTypeBuilder<FeedbackCriterionScore> e)
    {
        e.HasKey(x => new { x.FeedbackId, x.CriterionId });
        e.HasIndex(x => new { x.WorkspaceId, x.CriterionId });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Feedback).WithMany(f => f.CriterionScores)
            .HasForeignKey(x => new { x.WorkspaceId, x.FeedbackId })
            .HasPrincipalKey(f => new { f.WorkspaceId, f.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Criterion).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.CriterionId })
            .HasPrincipalKey(c => new { c.WorkspaceId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_FeedbackCriterionScores_Score", "\"Score\" BETWEEN 1 AND 5"));
    }
}

internal sealed class SubmissionReflectionConfiguration : IEntityTypeConfiguration<SubmissionReflection>
{
    public void Configure(EntityTypeBuilder<SubmissionReflection> e)
    {
        e.HasIndex(x => x.SubmissionId).IsUnique();
        e.HasOne(x => x.Submission).WithOne(s => s.Reflection)
            .HasForeignKey<SubmissionReflection>(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_SubmissionReflections_ConfidenceAfter", "\"ConfidenceAfter\" BETWEEN 1 AND 5"));
    }
}

internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> e)
    {
        e.Property(x => x.EventType)
            .HasConversion(new ValueConverter<ActivityEventType, string>(
                value => ToStorage(value),
                value => FromStorage(value)))
            .HasMaxLength(30)
            .IsRequired();
        e.Property(x => x.ReviewDuration).HasColumnType("interval");
        e.HasIndex(x => new { x.WorkspaceId, x.EventType, x.OccurredAtUtc });
        e.HasIndex(x => new { x.WorkspaceId, x.SubmissionId });
        e.HasIndex(x => new { x.WorkspaceId, x.AssignmentId })
            .IsUnique()
            .HasFilter("\"EventType\" = 'assignment_opened'");
        e.HasIndex(x => new { x.WorkspaceId, x.SubmissionId, x.EventType }).IsUnique();
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Assignment>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.AssignmentId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Submission>().WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.SubmissionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.MenteeUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.MentorUserId).OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ActivityEvents_Payload",
                "(\"EventType\" = 'assignment_opened' AND \"SubmissionId\" IS NULL AND \"ReviewDuration\" IS NULL AND NOT \"ExceedsTwoHours\") OR " +
                "(\"EventType\" = 'review_opened' AND \"SubmissionId\" IS NOT NULL AND \"ReviewDuration\" IS NULL AND NOT \"ExceedsTwoHours\") OR " +
                "(\"EventType\" = 'review_submitted' AND \"SubmissionId\" IS NOT NULL AND \"ReviewDuration\" IS NOT NULL " +
                "AND \"ReviewDuration\" >= INTERVAL '0 seconds' " +
                "AND \"ExceedsTwoHours\" = (\"ReviewDuration\" > INTERVAL '2 hours'))");
        });
    }

    private static string ToStorage(ActivityEventType eventType) => eventType switch
    {
        ActivityEventType.AssignmentOpened => "assignment_opened",
        ActivityEventType.ReviewOpened => "review_opened",
        ActivityEventType.ReviewSubmitted => "review_submitted",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
    };

    private static ActivityEventType FromStorage(string eventType) => eventType switch
    {
        "assignment_opened" => ActivityEventType.AssignmentOpened,
        "review_opened" => ActivityEventType.ReviewOpened,
        "review_submitted" => ActivityEventType.ReviewSubmitted,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null),
    };
}

internal sealed class AiGenerationRequestConfiguration : IEntityTypeConfiguration<AiGenerationRequest>
{
    public void Configure(EntityTypeBuilder<AiGenerationRequest> e)
    {
        e.Property(x => x.Provider).HasMaxLength(100).IsRequired();
        e.Property(x => x.Model).HasMaxLength(100).IsRequired();
        e.Property(x => x.PromptTemplateVersion).HasMaxLength(30).IsRequired();
        e.Property(x => x.ErrorCode).HasMaxLength(100);
        e.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Exercise>().WithMany().HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.SetNull);
    }
}
