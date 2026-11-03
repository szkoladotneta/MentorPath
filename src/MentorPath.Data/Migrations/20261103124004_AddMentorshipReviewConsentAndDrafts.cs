using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorshipReviewConsentAndDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MentorshipReviewConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisclosureVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MentorshipReviewConsents", x => x.Id);
                    table.CheckConstraint("CK_MentorshipReviewConsents_DisclosureVersion", "length(btrim(\"DisclosureVersion\")) > 0");
                    table.CheckConstraint("CK_MentorshipReviewConsents_RevokedAtUtc", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"GrantedAtUtc\"");
                    table.CheckConstraint("CK_MentorshipReviewConsents_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_MentorshipReviewConsents_Mentorships_WorkspaceId_Mentorship~",
                        columns: x => new { x.WorkspaceId, x.MentorshipId },
                        principalTable: "Mentorships",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MentorshipReviewConsents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    StrengthsMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    ImprovementsMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    ChallengeQuestionMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewDrafts", x => x.Id);
                    table.UniqueConstraint("AK_ReviewDrafts_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_ReviewDrafts_UpdatedAtUtc", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_ReviewDrafts_Verdict", "\"Verdict\" IS NULL OR \"Verdict\" IN ('Accepted', 'NeedsRevision')");
                    table.CheckConstraint("CK_ReviewDrafts_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ReviewDrafts_Submissions_WorkspaceId_SubmissionId",
                        columns: x => new { x.WorkspaceId, x.SubmissionId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewDrafts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewDraftCriterionScores",
                columns: table => new
                {
                    DraftId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<short>(type: "smallint", nullable: false),
                    CommentMarkdown = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewDraftCriterionScores", x => new { x.DraftId, x.CriterionId });
                    table.CheckConstraint("CK_ReviewDraftCriterionScores_Score", "\"Score\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_ReviewDraftCriterionScores_ReviewDrafts_WorkspaceId_DraftId",
                        columns: x => new { x.WorkspaceId, x.DraftId },
                        principalTable: "ReviewDrafts",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewDraftCriterionScores_RubricCriteria_WorkspaceId_Crite~",
                        columns: x => new { x.WorkspaceId, x.CriterionId },
                        principalTable: "RubricCriteria",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewDraftCriterionScores_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MentorshipReviewConsents_MentorshipId",
                table: "MentorshipReviewConsents",
                column: "MentorshipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorshipReviewConsents_WorkspaceId",
                table: "MentorshipReviewConsents",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_MentorshipReviewConsents_WorkspaceId_MentorshipId",
                table: "MentorshipReviewConsents",
                columns: new[] { "WorkspaceId", "MentorshipId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDraftCriterionScores_WorkspaceId",
                table: "ReviewDraftCriterionScores",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDraftCriterionScores_WorkspaceId_CriterionId",
                table: "ReviewDraftCriterionScores",
                columns: new[] { "WorkspaceId", "CriterionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDraftCriterionScores_WorkspaceId_DraftId",
                table: "ReviewDraftCriterionScores",
                columns: new[] { "WorkspaceId", "DraftId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDrafts_SubmissionId",
                table: "ReviewDrafts",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDrafts_WorkspaceId",
                table: "ReviewDrafts",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewDrafts_WorkspaceId_SubmissionId",
                table: "ReviewDrafts",
                columns: new[] { "WorkspaceId", "SubmissionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MentorshipReviewConsents");

            migrationBuilder.DropTable(
                name: "ReviewDraftCriterionScores");

            migrationBuilder.DropTable(
                name: "ReviewDrafts");
        }
    }
}
