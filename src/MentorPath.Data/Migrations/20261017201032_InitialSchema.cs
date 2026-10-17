using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workspaces_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invitations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Mentorships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: true),
                    CurrentLevel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    WeeklyHours = table.Column<int>(type: "integer", nullable: true),
                    Challenges = table.Column<string>(type: "text", nullable: true),
                    MentorPrivateNotes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mentorships", x => x.Id);
                    table.UniqueConstraint("AK_Mentorships_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Mentorships_WeeklyHours", "\"WeeklyHours\" IS NULL OR (\"WeeklyHours\" BETWEEN 1 AND 80)");
                    table.ForeignKey(
                        name: "FK_Mentorships_AspNetUsers_MenteeUserId",
                        column: x => x.MenteeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Mentorships_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DescriptionMarkdown = table.Column<string>(type: "text", nullable: false),
                    LearningObjectivesMarkdown = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                    table.UniqueConstraint("AK_Topics_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Topics_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roadmaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObjectiveMarkdown = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roadmaps", x => x.Id);
                    table.UniqueConstraint("AK_Roadmaps_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Roadmaps_Mentorships_WorkspaceId_MentorshipId",
                        columns: x => new { x.WorkspaceId, x.MentorshipId },
                        principalTable: "Mentorships",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Roadmaps_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScenarioMarkdown = table.Column<string>(type: "text", nullable: false),
                    InstructionsMarkdown = table.Column<string>(type: "text", nullable: false),
                    ExpectedOutcomeMarkdown = table.Column<string>(type: "text", nullable: true),
                    RubricMarkdown = table.Column<string>(type: "text", nullable: false),
                    DiscussionQuestionsMarkdown = table.Column<string>(type: "text", nullable: true),
                    MentorGuideMarkdown = table.Column<string>(type: "text", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "text", nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                    table.UniqueConstraint("AK_Exercises_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Exercises_EstimatedMinutes", "\"EstimatedMinutes\" IS NULL OR (\"EstimatedMinutes\" BETWEEN 1 AND 600)");
                    table.ForeignKey(
                        name: "FK_Exercises_Topics_WorkspaceId_TopicId",
                        columns: x => new { x.WorkspaceId, x.TopicId },
                        principalTable: "Topics",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Exercises_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoadmapItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    MentorNoteMarkdown = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoadmapItems", x => x.Id);
                    table.UniqueConstraint("AK_RoadmapItems_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_RoadmapItems_Roadmaps_WorkspaceId_RoadmapId",
                        columns: x => new { x.WorkspaceId, x.RoadmapId },
                        principalTable: "Roadmaps",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoadmapItems_Topics_WorkspaceId_TopicId",
                        columns: x => new { x.WorkspaceId, x.TopicId },
                        principalTable: "Topics",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoadmapItems_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiGenerationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExerciseType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromptTemplateVersion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuizOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IsCorrect = table.Column<bool>(type: "boolean", nullable: false),
                    ExplanationMarkdown = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizOptions_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                    table.UniqueConstraint("AK_Assignments_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Assignments_Exercises_WorkspaceId_ExerciseId",
                        columns: x => new { x.WorkspaceId, x.ExerciseId },
                        principalTable: "Exercises",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_RoadmapItems_WorkspaceId_RoadmapItemId",
                        columns: x => new { x.WorkspaceId, x.RoadmapItemId },
                        principalTable: "RoadmapItems",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assignments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SelectedQuizOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionAnswerJson = table.Column<string>(type: "jsonb", nullable: true),
                    AnswerMarkdown = table.Column<string>(type: "text", nullable: true),
                    ExternalUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConfidenceBefore = table.Column<short>(type: "smallint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.UniqueConstraint("AK_Submissions_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_Submissions_AttemptNumber", "\"AttemptNumber\" > 0");
                    table.CheckConstraint("CK_Submissions_ConfidenceBefore", "\"ConfidenceBefore\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Submissions_AspNetUsers_MenteeUserId",
                        column: x => x.MenteeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Assignments_WorkspaceId_AssignmentId",
                        columns: x => new { x.WorkspaceId, x.AssignmentId },
                        principalTable: "Assignments",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_QuizOptions_SelectedQuizOptionId",
                        column: x => x.SelectedQuizOptionId,
                        principalTable: "QuizOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MentorFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StrengthsMarkdown = table.Column<string>(type: "text", nullable: false),
                    ImprovementsMarkdown = table.Column<string>(type: "text", nullable: false),
                    ChallengeQuestionMarkdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MentorFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MentorFeedback_AspNetUsers_MentorUserId",
                        column: x => x.MentorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MentorFeedback_Submissions_WorkspaceId_SubmissionId",
                        columns: x => new { x.WorkspaceId, x.SubmissionId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MentorFeedback_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionAttachments_Submissions_WorkspaceId_SubmissionId",
                        columns: x => new { x.WorkspaceId, x.SubmissionId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubmissionAttachments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionReflections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfidenceAfter = table.Column<short>(type: "smallint", nullable: false),
                    TakeawayMarkdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionReflections", x => x.Id);
                    table.CheckConstraint("CK_SubmissionReflections_ConfidenceAfter", "\"ConfidenceAfter\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_SubmissionReflections_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_ExerciseId",
                table: "AiGenerationRequests",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_RequestedByUserId",
                table: "AiGenerationRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_WorkspaceId_CreatedAtUtc",
                table: "AiGenerationRequests",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_RoadmapItemId_ExerciseId",
                table: "Assignments",
                columns: new[] { "RoadmapItemId", "ExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_WorkspaceId",
                table: "Assignments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_WorkspaceId_ExerciseId",
                table: "Assignments",
                columns: new[] { "WorkspaceId", "ExerciseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_WorkspaceId_RoadmapItemId",
                table: "Assignments",
                columns: new[] { "WorkspaceId", "RoadmapItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_WorkspaceId_TopicId_Status",
                table: "Exercises",
                columns: new[] { "WorkspaceId", "TopicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InvitedByUserId",
                table: "Invitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                table: "Invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_WorkspaceId_NormalizedEmail",
                table: "Invitations",
                columns: new[] { "WorkspaceId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_MentorFeedback_MentorUserId",
                table: "MentorFeedback",
                column: "MentorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MentorFeedback_SubmissionId",
                table: "MentorFeedback",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MentorFeedback_WorkspaceId",
                table: "MentorFeedback",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_MentorFeedback_WorkspaceId_SubmissionId",
                table: "MentorFeedback",
                columns: new[] { "WorkspaceId", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mentorships_MenteeUserId",
                table: "Mentorships",
                column: "MenteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Mentorships_WorkspaceId_MenteeUserId",
                table: "Mentorships",
                columns: new[] { "WorkspaceId", "MenteeUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_ExerciseId_Position",
                table: "QuizOptions",
                columns: new[] { "ExerciseId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizOptions_ExerciseId_SingleCorrect",
                table: "QuizOptions",
                column: "ExerciseId",
                unique: true,
                filter: "\"IsCorrect\"");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_RoadmapId_TopicId",
                table: "RoadmapItems",
                columns: new[] { "RoadmapId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_WorkspaceId",
                table: "RoadmapItems",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_WorkspaceId_RoadmapId",
                table: "RoadmapItems",
                columns: new[] { "WorkspaceId", "RoadmapId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_WorkspaceId_TopicId",
                table: "RoadmapItems",
                columns: new[] { "WorkspaceId", "TopicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_MentorshipId",
                table: "Roadmaps",
                column: "MentorshipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_WorkspaceId_MentorshipId",
                table: "Roadmaps",
                columns: new[] { "WorkspaceId", "MentorshipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAttachments_ObjectKey",
                table: "SubmissionAttachments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAttachments_SubmissionId",
                table: "SubmissionAttachments",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAttachments_WorkspaceId",
                table: "SubmissionAttachments",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAttachments_WorkspaceId_SubmissionId",
                table: "SubmissionAttachments",
                columns: new[] { "WorkspaceId", "SubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionReflections_SubmissionId",
                table: "SubmissionReflections",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_AssignmentId_AttemptNumber",
                table: "Submissions",
                columns: new[] { "AssignmentId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_AssignmentId_SinglePending",
                table: "Submissions",
                column: "AssignmentId",
                unique: true,
                filter: "\"Status\" = 'PendingReview'");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_MenteeUserId_ClientRequestId",
                table: "Submissions",
                columns: new[] { "MenteeUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_SelectedQuizOptionId",
                table: "Submissions",
                column: "SelectedQuizOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_WorkspaceId_AssignmentId",
                table: "Submissions",
                columns: new[] { "WorkspaceId", "AssignmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_WorkspaceId_Status_SubmittedAtUtc",
                table: "Submissions",
                columns: new[] { "WorkspaceId", "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Topics_WorkspaceId_Slug",
                table: "Topics",
                columns: new[] { "WorkspaceId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerUserId",
                table: "Workspaces",
                column: "OwnerUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGenerationRequests");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "MentorFeedback");

            migrationBuilder.DropTable(
                name: "SubmissionAttachments");

            migrationBuilder.DropTable(
                name: "SubmissionReflections");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropTable(
                name: "Assignments");

            migrationBuilder.DropTable(
                name: "QuizOptions");

            migrationBuilder.DropTable(
                name: "RoadmapItems");

            migrationBuilder.DropTable(
                name: "Exercises");

            migrationBuilder.DropTable(
                name: "Roadmaps");

            migrationBuilder.DropTable(
                name: "Topics");

            migrationBuilder.DropTable(
                name: "Mentorships");

            migrationBuilder.DropTable(
                name: "Workspaces");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
