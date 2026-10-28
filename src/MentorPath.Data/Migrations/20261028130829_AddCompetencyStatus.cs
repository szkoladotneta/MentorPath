using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetencyStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompetencyStatus",
                table: "RoadmapItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "NotAssessed");

            migrationBuilder.CreateTable(
                name: "CompetencyStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NewStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ManualJustification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencyStatusHistory", x => x.Id);
                    table.UniqueConstraint("AK_CompetencyStatusHistory_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_CompetencyStatusHistory_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetencyStatusHistory_RoadmapItems_WorkspaceId_RoadmapIte~",
                        columns: x => new { x.WorkspaceId, x.RoadmapItemId },
                        principalTable: "RoadmapItems",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetencyStatusHistory_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyStatusHistory_ChangedByUserId",
                table: "CompetencyStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyStatusHistory_WorkspaceId",
                table: "CompetencyStatusHistory",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetencyStatusHistory_WorkspaceId_RoadmapItemId_ChangedAt~",
                table: "CompetencyStatusHistory",
                columns: new[] { "WorkspaceId", "RoadmapItemId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetencyStatusHistory");

            migrationBuilder.DropColumn(
                name: "CompetencyStatus",
                table: "RoadmapItems");
        }
    }
}
