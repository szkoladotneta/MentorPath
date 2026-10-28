using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PageContext = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductFeedback_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFeedback_AuthorUserId_CreatedAtUtc",
                table: "ProductFeedback",
                columns: new[] { "AuthorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductFeedback_CreatedAtUtc",
                table: "ProductFeedback",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductFeedback");
        }
    }
}
