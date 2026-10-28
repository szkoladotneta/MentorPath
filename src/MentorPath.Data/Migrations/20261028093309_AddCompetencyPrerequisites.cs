using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetencyPrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetencyPrerequisites",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrerequisiteTopicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencyPrerequisites", x => new { x.WorkspaceId, x.TopicId, x.PrerequisiteTopicId });
                    table.CheckConstraint("CK_CompetencyPrerequisites_NotSelf", "\"TopicId\" <> \"PrerequisiteTopicId\"");
                    table.ForeignKey(
                        name: "FK_CompetencyPrerequisites_Topics_WorkspaceId_PrerequisiteTopi~",
                        columns: x => new { x.WorkspaceId, x.PrerequisiteTopicId },
                        principalTable: "Topics",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetencyPrerequisites_Topics_WorkspaceId_TopicId",
                        columns: x => new { x.WorkspaceId, x.TopicId },
                        principalTable: "Topics",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetencyPrerequisites_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyPrerequisites_WorkspaceId",
                table: "CompetencyPrerequisites",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyPrerequisites_WorkspaceId_PrerequisiteTopicId",
                table: "CompetencyPrerequisites",
                columns: new[] { "WorkspaceId", "PrerequisiteTopicId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetencyPrerequisites");
        }
    }
}
