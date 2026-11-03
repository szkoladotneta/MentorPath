using System.Text.Json;
using System.Text.Json.Serialization;
using MentorPath.Application.Privacy.Contracts;
using MentorPath.Data.Enums;

namespace MentorPath.UnitTests.Privacy;

public sealed class PersonalDataExportContractTests
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateTime GrantedAtUtc = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Export_preserves_legacy_constructor_and_JSON_shape_when_new_collections_are_omitted()
    {
        var export = LegacyExport();
        export.ReviewConsents.ShouldBeNull();
        export.ReviewDrafts.ShouldBeNull();

        var json = JsonSerializer.SerializeToElement(export, ExportJson);
        json.EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(
            ["schemaVersion", "generatedAtUtc", "profile", "mentorships", "submissions", "invitations", "productFeedback"]);
        json.GetProperty("schemaVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void Export_reads_legacy_JSON_without_consent_or_draft_fields()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "generatedAtUtc": "2026-11-03T10:00:00Z",
              "profile": {
                "userId": "00000000-0000-0000-0000-000000000001",
                "email": "subject@example.invalid",
                "displayName": "Subject",
                "createdAtUtc": "2026-11-03T10:00:00Z",
                "roles": ["Mentee"]
              },
              "mentorships": [],
              "submissions": [],
              "invitations": [],
              "productFeedback": []
            }
            """;

        var export = JsonSerializer.Deserialize<PersonalDataExport>(json, ExportJson);
        export.ShouldNotBeNull();
        export.SchemaVersion.ShouldBe(2);
        export.Profile.UserId.ShouldBe(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        export.ReviewConsents.ShouldBeNull();
        export.ReviewDrafts.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Export_round_trips_latest_raw_consent_evidence_and_complete_unpublished_draft(bool revoked)
    {
        var mentorshipId = Guid.NewGuid();
        var consent = new MentorshipReviewConsentExport(
            Guid.NewGuid(), mentorshipId, "mcp-review-v1", GrantedAtUtc,
            revoked ? GrantedAtUtc.AddHours(1) : null, 2);
        var score = new ReviewDraftCriterionScoreExport(Guid.NewGuid(), "Architecture", 3, 4, "Draft criterion comment");
        var draft = new ReviewDraftExport(
            Guid.NewGuid(), Guid.NewGuid(), mentorshipId, ReviewVerdict.NeedsRevision,
            "Draft strengths", "Draft improvements", "Draft question",
            GrantedAtUtc, GrantedAtUtc.AddMinutes(30), 7, [score]);
        var export = LegacyExport() with { ReviewConsents = [consent], ReviewDrafts = [draft] };

        var json = JsonSerializer.SerializeToElement(export, ExportJson);
        var consentJson = json.GetProperty("reviewConsents")[0];
        consentJson.EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(
            ["consentId", "mentorshipId", "disclosureVersion", "grantedAtUtc", "revokedAtUtc", "version"]);
        var draftJson = json.GetProperty("reviewDrafts")[0];
        draftJson.EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(
            ["draftId", "submissionId", "mentorshipId", "verdict", "strengthsMarkdown", "improvementsMarkdown",
                "challengeQuestionMarkdown", "createdAtUtc", "updatedAtUtc", "version", "criterionScores"]);
        draftJson.GetProperty("verdict").GetString().ShouldBe("NeedsRevision");
        draftJson.GetProperty("criterionScores")[0].EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(
            ["criterionId", "criterionName", "position", "score", "commentMarkdown"]);

        var restored = json.Deserialize<PersonalDataExport>(ExportJson);
        restored.ShouldNotBeNull();
        restored.ReviewConsents.ShouldNotBeNull();
        restored.ReviewConsents.ShouldHaveSingleItem().ShouldBe(consent);
        restored.ReviewDrafts.ShouldNotBeNull();
        var restoredDraft = restored.ReviewDrafts.ShouldHaveSingleItem();
        restoredDraft.CriterionScores.ShouldHaveSingleItem().ShouldBe(score);
        (restoredDraft with { CriterionScores = draft.CriterionScores }).ShouldBe(draft);
    }

    [Fact]
    public void Export_round_trips_partial_drafts_without_inventing_text_verdict_or_scores()
    {
        var draft = new ReviewDraftExport(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            null, "", null, GrantedAtUtc, GrantedAtUtc, 1, []);
        var export = LegacyExport() with { ReviewConsents = [], ReviewDrafts = [draft] };

        var json = JsonSerializer.SerializeToElement(export, ExportJson);
        json.GetProperty("reviewConsents").GetArrayLength().ShouldBe(0);
        var restored = json.Deserialize<PersonalDataExport>(ExportJson);
        restored.ShouldNotBeNull();
        restored.ReviewConsents.ShouldNotBeNull();
        restored.ReviewConsents.ShouldBeEmpty();
        restored.ReviewDrafts.ShouldNotBeNull();
        var restoredDraft = restored.ReviewDrafts.ShouldHaveSingleItem();
        restoredDraft.CriterionScores.ShouldBeEmpty();
        (restoredDraft with { CriterionScores = draft.CriterionScores }).ShouldBe(draft);
    }

    [Fact]
    public void Export_distinguishes_populated_empty_collections_from_legacy_missing_data()
    {
        var export = LegacyExport() with { ReviewConsents = [], ReviewDrafts = [] };

        var json = JsonSerializer.SerializeToElement(export, ExportJson);
        json.GetProperty("reviewConsents").GetArrayLength().ShouldBe(0);
        json.GetProperty("reviewDrafts").GetArrayLength().ShouldBe(0);
        var restored = json.Deserialize<PersonalDataExport>(ExportJson);
        restored.ShouldNotBeNull();
        restored.ReviewConsents.ShouldNotBeNull();
        restored.ReviewConsents.ShouldBeEmpty();
        restored.ReviewDrafts.ShouldNotBeNull();
        restored.ReviewDrafts.ShouldBeEmpty();
    }

    private static PersonalDataExport LegacyExport() => new(
        2, GrantedAtUtc,
        new ProfileExport(Guid.NewGuid(), "subject@example.invalid", "Subject", GrantedAtUtc, ["Mentee"]),
        [], [], [], []);
}
