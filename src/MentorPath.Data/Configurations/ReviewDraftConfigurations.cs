using MentorPath.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MentorPath.Data.Configurations;

internal sealed class MentorshipReviewConsentConfiguration : IEntityTypeConfiguration<MentorshipReviewConsent>
{
    public void Configure(EntityTypeBuilder<MentorshipReviewConsent> e)
    {
        e.Property(x => x.DisclosureVersion).HasMaxLength(100).IsRequired();
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.MentorshipId).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Mentorship).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.MentorshipId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_MentorshipReviewConsents_DisclosureVersion", "length(btrim(\"DisclosureVersion\")) > 0");
            t.HasCheckConstraint("CK_MentorshipReviewConsents_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_MentorshipReviewConsents_RevokedAtUtc", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"GrantedAtUtc\"");
        });
    }
}

internal sealed class ReviewDraftConfiguration : IEntityTypeConfiguration<ReviewDraft>
{
    public void Configure(EntityTypeBuilder<ReviewDraft> e)
    {
        e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        e.Property(x => x.StrengthsMarkdown).HasMaxLength(20_000);
        e.Property(x => x.ImprovementsMarkdown).HasMaxLength(20_000);
        e.Property(x => x.ChallengeQuestionMarkdown).HasMaxLength(20_000);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.SubmissionId).IsUnique();
        e.HasIndex(x => x.WorkspaceId);
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Submission).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.SubmissionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ReviewDrafts_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_ReviewDrafts_UpdatedAtUtc", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
            t.HasCheckConstraint("CK_ReviewDrafts_Verdict", "\"Verdict\" IS NULL OR \"Verdict\" IN ('Accepted', 'NeedsRevision')");
        });
    }
}

internal sealed class ReviewDraftCriterionScoreConfiguration : IEntityTypeConfiguration<ReviewDraftCriterionScore>
{
    public void Configure(EntityTypeBuilder<ReviewDraftCriterionScore> e)
    {
        e.HasKey(x => new { x.DraftId, x.CriterionId });
        e.Property(x => x.CommentMarkdown).HasMaxLength(20_000).IsRequired();
        e.HasIndex(x => x.WorkspaceId);
        e.HasIndex(x => new { x.WorkspaceId, x.CriterionId });
        e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Draft).WithMany(x => x.CriterionScores)
            .HasForeignKey(x => new { x.WorkspaceId, x.DraftId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Criterion).WithMany()
            .HasForeignKey(x => new { x.WorkspaceId, x.CriterionId })
            .HasPrincipalKey(x => new { x.WorkspaceId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.ToTable(t => t.HasCheckConstraint("CK_ReviewDraftCriterionScores_Score", "\"Score\" BETWEEN 1 AND 5"));
    }
}
