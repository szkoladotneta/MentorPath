using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDecisionRubric : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_MentorFeedback_WorkspaceId_Id",
                table: "MentorFeedback",
                columns: new[] { "WorkspaceId", "Id" });

            migrationBuilder.CreateTable(
                name: "RubricCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Level1Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level2Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level3Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level4Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level5Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricCriteria", x => x.Id);
                    table.UniqueConstraint("AK_RubricCriteria_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                    table.CheckConstraint("CK_RubricCriteria_Position", "\"Position\" >= 0");
                    table.ForeignKey(
                        name: "FK_RubricCriteria_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExerciseCriteria",
                columns: table => new
                {
                    ExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExerciseCriteria", x => new { x.ExerciseId, x.CriterionId });
                    table.ForeignKey(
                        name: "FK_ExerciseCriteria_Exercises_WorkspaceId_ExerciseId",
                        columns: x => new { x.WorkspaceId, x.ExerciseId },
                        principalTable: "Exercises",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExerciseCriteria_RubricCriteria_WorkspaceId_CriterionId",
                        columns: x => new { x.WorkspaceId, x.CriterionId },
                        principalTable: "RubricCriteria",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExerciseCriteria_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackCriterionScores",
                columns: table => new
                {
                    FeedbackId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<short>(type: "smallint", nullable: false),
                    CommentMarkdown = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackCriterionScores", x => new { x.FeedbackId, x.CriterionId });
                    table.CheckConstraint("CK_FeedbackCriterionScores_Score", "\"Score\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_FeedbackCriterionScores_MentorFeedback_WorkspaceId_Feedback~",
                        columns: x => new { x.WorkspaceId, x.FeedbackId },
                        principalTable: "MentorFeedback",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackCriterionScores_RubricCriteria_WorkspaceId_Criterio~",
                        columns: x => new { x.WorkspaceId, x.CriterionId },
                        principalTable: "RubricCriteria",
                        principalColumns: new[] { "WorkspaceId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FeedbackCriterionScores_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseCriteria_WorkspaceId_CriterionId",
                table: "ExerciseCriteria",
                columns: new[] { "WorkspaceId", "CriterionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExerciseCriteria_WorkspaceId_ExerciseId",
                table: "ExerciseCriteria",
                columns: new[] { "WorkspaceId", "ExerciseId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackCriterionScores_WorkspaceId_CriterionId",
                table: "FeedbackCriterionScores",
                columns: new[] { "WorkspaceId", "CriterionId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackCriterionScores_WorkspaceId_FeedbackId",
                table: "FeedbackCriterionScores",
                columns: new[] { "WorkspaceId", "FeedbackId" });

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriteria_WorkspaceId_Name",
                table: "RubricCriteria",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            // D-43: seed the 6 default criteria for every existing workspace and select all of them for every
            // reviewable exercise. RubricMarkdown is left untouched.
            migrationBuilder.Sql("""
                INSERT INTO "RubricCriteria" ("Id", "WorkspaceId", "Name", "Position", "IsActive",
                    "Level1Description", "Level2Description", "Level3Description", "Level4Description", "Level5Description", "CreatedAtUtc")
                SELECT gen_random_uuid(), w."Id", d."Name", d."Position", TRUE,
                    d."L1", d."L2", d."L3", d."L4", d."L5", now()
                FROM "Workspaces" w
                CROSS JOIN (VALUES
                    ('Rozpoznanie ograniczeń', 0,
                     'Nie wskazuje ograniczeń ani wymagań problemu.',
                     'Wskazuje pojedyncze ograniczenia, pomija kluczowe (skala, spójność, koszt, zespół).',
                     'Rozpoznaje główne ograniczenia, ale bez priorytetów i bez weryfikacji założeń.',
                     'Rozpoznaje i priorytetyzuje ograniczenia oraz jawnie nazywa założenia.',
                     'Pełny obraz ograniczeń z priorytetami, założeniami i wskazaniem, co trzeba doprecyzować.'),
                    ('Alternatywy', 1,
                     'Przedstawia tylko jedno rozwiązanie.',
                     'Wspomina o alternatywie, ale bez sensownego opisu.',
                     'Opisuje co najmniej dwie realne opcje.',
                     'Opisuje realne opcje i uzasadnia, dlaczego odrzuca pozostałe.',
                     'Systematycznie porównuje opcje, w tym „nic nie zmieniaj” i rozwiązania prostsze.'),
                    ('Trade-offs', 2,
                     'Nie wskazuje kosztów ani wad wybranego rozwiązania.',
                     'Wymienia ogólne wady bez powiązania z kontekstem.',
                     'Wskazuje główne kompromisy wybranego rozwiązania.',
                     'Wskazuje kompromisy w kontekście ograniczeń i uzasadnia ich akceptację.',
                     'Waży kompromisy i pokazuje, co świadomie zyskujemy, a co tracimy.'),
                    ('Ewolucja rozwiązania', 3,
                     'Brak myślenia o rozwoju rozwiązania w czasie.',
                     'Wspomina o przyszłych zmianach bez konkretów.',
                     'Opisuje, jak rozwiązanie może się zmieniać przy wzroście wymagań.',
                     'Wskazuje sygnały zmiany i bezpieczne kroki migracji.',
                     'Planuje ewolucję: wyraźne triggery zmiany, koszt migracji i odwracalność decyzji.'),
                    ('Operacyjność', 4,
                     'Nie uwzględnia wdrożenia ani utrzymania.',
                     'Wspomina o monitoringu lub wdrożeniu bez szczegółów.',
                     'Uwzględnia wdrożenie, monitoring i obsługę błędów na podstawowym poziomie.',
                     'Opisuje obserwowalność, scenariusze awarii i sposób odzyskiwania.',
                     'Kompleksowo: failure modes, SLO, koszty utrzymania, rollback i obciążenie zespołu.'),
                    ('Komunikacja', 5,
                     'Odpowiedź chaotyczna, trudna do zrozumienia.',
                     'Zrozumiała w części, brak struktury.',
                     'Czytelna i uporządkowana, ale bez dostosowania do odbiorcy.',
                     'Jasna, ustrukturyzowana, z uzasadnieniem decyzji dla odbiorcy.',
                     'Zwięzła i przekonująca; oddziela fakty, założenia i rekomendację oraz wskazuje ryzyka.')
                ) AS d("Name", "Position", "L1", "L2", "L3", "L4", "L5");
                """);

            migrationBuilder.Sql("""
                INSERT INTO "ExerciseCriteria" ("WorkspaceId", "ExerciseId", "CriterionId")
                SELECT e."WorkspaceId", e."Id", c."Id"
                FROM "Exercises" e
                JOIN "RubricCriteria" c ON c."WorkspaceId" = e."WorkspaceId"
                WHERE e."Type" <> 'QuizSingleChoice';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExerciseCriteria");

            migrationBuilder.DropTable(
                name: "FeedbackCriterionScores");

            migrationBuilder.DropTable(
                name: "RubricCriteria");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MentorFeedback_WorkspaceId_Id",
                table: "MentorFeedback");
        }
    }
}
