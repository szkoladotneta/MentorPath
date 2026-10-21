using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTopicParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentTopicId",
                table: "Topics",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_WorkspaceId_ParentTopicId",
                table: "Topics",
                columns: new[] { "WorkspaceId", "ParentTopicId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Topics_NotOwnParent",
                table: "Topics",
                sql: "\"ParentTopicId\" IS NULL OR \"ParentTopicId\" <> \"Id\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Topics_Topics_WorkspaceId_ParentTopicId",
                table: "Topics",
                columns: new[] { "WorkspaceId", "ParentTopicId" },
                principalTable: "Topics",
                principalColumns: new[] { "WorkspaceId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Topics_Topics_WorkspaceId_ParentTopicId",
                table: "Topics");

            migrationBuilder.DropIndex(
                name: "IX_Topics_WorkspaceId_ParentTopicId",
                table: "Topics");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Topics_NotOwnParent",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "ParentTopicId",
                table: "Topics");
        }
    }
}
