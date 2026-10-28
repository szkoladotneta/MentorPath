using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MentorPath.Data.Migrations
{
    /// <inheritdoc />
    public partial class MigrateDecisionAnswerJsonV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Submissions"
                SET "DecisionAnswerJson" = jsonb_build_object(
                    'schemaVersion', 2,
                    'answer', jsonb_build_object(
                        'decision', "DecisionAnswerJson" -> 'decision',
                        'rationale', "DecisionAnswerJson" -> 'rationale',
                        'alternatives', "DecisionAnswerJson" -> 'alternatives',
                        'tradeOffs', "DecisionAnswerJson" -> 'tradeOffs',
                        'risks', "DecisionAnswerJson" -> 'risks',
                        'reconsiderationConditions', "DecisionAnswerJson" -> 'reconsiderationConditions'))
                WHERE jsonb_typeof("DecisionAnswerJson") = 'object'
                  AND "DecisionAnswerJson" ->> 'schemaVersion' = '1';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "Submissions"
                SET "DecisionAnswerJson" = jsonb_build_object(
                    'schemaVersion', 1,
                    'decision', "DecisionAnswerJson" -> 'answer' -> 'decision',
                    'rationale', "DecisionAnswerJson" -> 'answer' -> 'rationale',
                    'alternatives', "DecisionAnswerJson" -> 'answer' -> 'alternatives',
                    'tradeOffs', "DecisionAnswerJson" -> 'answer' -> 'tradeOffs',
                    'risks', "DecisionAnswerJson" -> 'answer' -> 'risks',
                    'reconsiderationConditions', "DecisionAnswerJson" -> 'answer' -> 'reconsiderationConditions')
                WHERE jsonb_typeof("DecisionAnswerJson") = 'object'
                  AND "DecisionAnswerJson" ->> 'schemaVersion' = '2'
                  AND jsonb_typeof("DecisionAnswerJson" -> 'answer') = 'object';
                """);
        }
    }
}
