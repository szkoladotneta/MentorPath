using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MenteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ExceedsTwoHours = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                    table.CheckConstraint("CK_ActivityEvents_Payload", "(\"EventType\" = 'assignment_opened' AND \"SubmissionId\" IS NULL AND \"ReviewDuration\" IS NULL AND NOT \"ExceedsTwoHours\") OR (\"EventType\" = 'review_opened' AND \"SubmissionId\" IS NOT NULL AND \"ReviewDuration\" IS NULL AND NOT \"ExceedsTwoHours\") OR (\"EventType\" = 'review_submitted' AND \"SubmissionId\" IS NOT NULL AND \"ReviewDuration\" IS NOT NULL AND \"ReviewDuration\" >= INTERVAL '0 seconds' AND \"ExceedsTwoHours\" = (\"ReviewDuration\" > INTERVAL '2 hours'))");
                    table.ForeignKey(
                        name: "FK_ActivityEvents_AspNetUsers_MenteeUserId",
                        column: x => x.MenteeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_AspNetUsers_MentorUserId",
                        column: x => x.MentorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Assignments_WorkspaceId_AssignmentId",
                        columns: x => new { x.WorkspaceId, x.AssignmentId },
                        principalTable: "Assignments",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Submissions_WorkspaceId_SubmissionId",
                        columns: x => new { x.WorkspaceId, x.SubmissionId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_MenteeUserId",
                table: "ActivityEvents",
                column: "MenteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_MentorUserId",
                table: "ActivityEvents",
                column: "MentorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_WorkspaceId_AssignmentId",
                table: "ActivityEvents",
                columns: new[] { "WorkspaceId", "AssignmentId" },
                unique: true,
                filter: "\"EventType\" = 'assignment_opened'");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_WorkspaceId_EventType_OccurredAtUtc",
                table: "ActivityEvents",
                columns: new[] { "WorkspaceId", "EventType", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_WorkspaceId_SubmissionId",
                table: "ActivityEvents",
                columns: new[] { "WorkspaceId", "SubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_WorkspaceId_SubmissionId_EventType",
                table: "ActivityEvents",
                columns: new[] { "WorkspaceId", "SubmissionId", "EventType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");
        }
    }
}
