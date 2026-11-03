using System.Security.Cryptography;
using System.Text;
using MentorPath.Application.Common;
using MentorPath.Application.Mentorships;
using MentorPath.Application.Mentorships.Contracts;
using MentorPath.Data.Entities;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MentorPath.IntegrationTests.Mentorships;

public sealed class MentorshipReviewConsentTests(PostgresFixture db) : IntegrationTestBase(db)
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Mcp:Reviews:Enabled"] = "true",
        ["Mcp:Reviews:ProviderNotice"] = "Operator-approved test provider; answers, files, reviews and reflections are disclosed."
    };

    private IMentorshipReviewConsentService Consent => App.Get<IMentorshipReviewConsentService>();
    private McpReviewOptions Options => App.Get<IOptionsMonitor<McpReviewOptions>>().CurrentValue;

    [Theory]
    [InlineData(MentorshipStatus.Active)]
    [InlineData(MentorshipStatus.Paused)]
    public async Task Consent_requires_an_explicit_grant_and_supports_versioned_revoke_and_regrant(MentorshipStatus status)
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, status, default)).ShouldSucceed();

        var absent = (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        absent.ShouldBe(new(mentee.MentorshipId, null, null, null, null));
        (await Consent.GetAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(absent);
        (await App.Db(d => d.MentorshipReviewConsents.AnyAsync(c => c.MentorshipId == mentee.MentorshipId))).ShouldBeFalse();

        var granted = (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        granted.Version.ShouldBe(1);
        granted.GrantedAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);
        granted.RevokedAtUtc.ShouldBeNull();
        (await Consent.GetAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(granted);

        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var revoked = (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 1), default)).ShouldSucceed();
        revoked.Version.ShouldBe(2);
        revoked.GrantedAtUtc.ShouldBe(granted.GrantedAtUtc);
        revoked.RevokedAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);

        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var regranted = (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 2), default)).ShouldSucceed();
        regranted.Version.ShouldBe(3);
        regranted.GrantedAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);
        regranted.RevokedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_GrantAsync_and_RevokeAsync_reject_foreign_actors_and_unknown_resources()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var foreignMentor = await Data.MentorAsync();
        var foreignMentee = await Data.MenteeAsync(foreignMentor);
        var sameWorkspaceMentee = await Data.MenteeAsync(mentor);
        var granted = (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();

        foreach (var actor in new[] { foreignMentor.UserId, foreignMentee.UserId, sameWorkspaceMentee.UserId, Guid.NewGuid() })
        {
            (await Consent.GetAsync(actor, mentee.MentorshipId, default)).ShouldBeNotFound();
            (await Consent.GrantAsync(actor, new(mentee.MentorshipId, Options.DisclosureVersion, 1), default)).ShouldBeNotFound();
            (await Consent.RevokeAsync(actor, new(mentee.MentorshipId, 1), default)).ShouldBeNotFound();
        }
        (await Consent.GrantAsync(mentor.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 1), default)).ShouldBeNotFound();
        (await Consent.RevokeAsync(mentor.UserId, new(mentee.MentorshipId, 1), default)).ShouldBeNotFound();
        var missing = Guid.NewGuid();
        (await Consent.GetAsync(mentor.UserId, missing, default)).ShouldBeNotFound();
        (await Consent.GrantAsync(mentee.UserId, new(missing, Options.DisclosureVersion, null), default)).ShouldBeNotFound();
        (await Consent.RevokeAsync(mentee.UserId, new(missing, 1), default)).ShouldBeNotFound();
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(granted);
    }

    [Fact]
    public async Task Disabled_by_default_prevents_grants_but_existing_state_and_revocation_remain_available()
    {
        await using var disabledApp = await TestApp.CreateAsync(Database);
        disabledApp.Get<IOptionsMonitor<McpReviewOptions>>().CurrentValue.Enabled.ShouldBeFalse();
        var mentor = await disabledApp.Data.MentorAsync();
        var mentee = await disabledApp.Data.MenteeAsync(mentor);
        var disabledConsent = disabledApp.Get<IMentorshipReviewConsentService>();
        var version = disabledApp.Get<IOptionsMonitor<McpReviewOptions>>().CurrentValue.DisclosureVersion;
        (await disabledConsent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, version, null), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_disabled");
        (await disabledConsent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().Version.ShouldBeNull();

        var enabledMentor = await Data.MentorAsync();
        var enabledMentee = await Data.MenteeAsync(enabledMentor);
        var grant = (await Consent.GrantAsync(enabledMentee.UserId, new(enabledMentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        SetOptions(new McpReviewOptions());
        (await Consent.GetAsync(enabledMentee.UserId, enabledMentee.MentorshipId, default)).ShouldSucceed().ShouldBe(grant);
        (await Consent.GrantAsync(enabledMentee.UserId, new(enabledMentee.MentorshipId, Options.DisclosureVersion, 1), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_disabled");
        (await Consent.RevokeAsync(enabledMentee.UserId, new(enabledMentee.MentorshipId, 1), default))
            .ShouldSucceed().Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mcp-review-v1-old")]
    public async Task Grant_rejects_any_disclosure_other_than_the_exact_current_notice(string? disclosureVersion)
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, disclosureVersion!, null), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_disclosure_changed");
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().Version.ShouldBeNull();
    }

    [Fact]
    public async Task Changing_the_provider_notice_requires_a_new_disclosure_and_an_explicit_versioned_grant()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var oldDisclosure = Options.DisclosureVersion;
        var original = (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, oldDisclosure, null), default)).ShouldSucceed();
        SetOptions(new McpReviewOptions { Enabled = true, ProviderNotice = "Different operator-approved test provider." });
        Options.DisclosureVersion.ShouldNotBe(oldDisclosure);
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, oldDisclosure, 1), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_disclosure_changed");
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(original);
        var updated = (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 1), default)).ShouldSucceed();
        updated.DisclosureVersion.ShouldBe(Options.DisclosureVersion);
        updated.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Expected_version_is_create_only_when_null_and_updates_require_the_current_positive_version()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        foreach (var version in new long[] { -1, 0, 1 })
        {
            (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, version), default))
                .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
            (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, version), default))
                .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        }
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        foreach (var version in new long?[] { null, -1, 0, 2 })
        {
            (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, version), default))
                .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        }
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 2), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 1), default)).ShouldSucceed();
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 1), default))
            .ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 2), default)).ShouldSucceed().Version.ShouldBe(3);
    }

    [Fact]
    public async Task Concurrent_initial_grants_create_only_one_consent_and_the_loser_gets_a_conflict()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var request = new GrantMentorshipReviewConsentRequest(mentee.MentorshipId, Options.DisclosureVersion, null);
        var results = await Task.WhenAll(Consent.GrantAsync(mentee.UserId, request, default), Consent.GrantAsync(mentee.UserId, request, default));
        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => !r.IsSuccess).ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        (await App.Db(d => d.MentorshipReviewConsents.CountAsync(c => c.MentorshipId == mentee.MentorshipId))).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_revoke_and_regrant_cannot_both_mutate_the_same_version()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        var results = await Task.WhenAll(
            Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 1), default),
            Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 1), default));
        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => !r.IsSuccess).ShouldFail(ErrorKind.Conflict, "mentorship.review_consent_version_conflict");
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().Version.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_close_and_regrant_always_leave_the_closed_mentorship_revoked()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        var closing = App.Get<IMentorshipService>().ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default);
        var granting = Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 1), default);
        await Task.WhenAll(closing, granting);
        (await closing).ShouldSucceed();
        if (!(await granting).IsSuccess) (await granting).ShouldBeNotFound();
        var final = (await Consent.GetAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        final.RevokedAtUtc.ShouldNotBeNull();
        final.Version.ShouldBe((await granting).IsSuccess ? 3 : 2);
    }

    [Fact]
    public async Task Closing_revokes_a_grant_once_and_reopening_does_not_restore_it_or_delete_drafts()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        var assignment = await Data.PublishedAssignmentAsync(mentor, mentee, ExerciseType.ArchitecturalDecision);
        var submission = await Data.DecisionSubmissionAsync(mentee, assignment.AssignmentId);
        var draftId = Guid.NewGuid();
        await App.Db(async d =>
        {
            d.ReviewDrafts.Add(new ReviewDraft
            {
                Id = draftId, WorkspaceId = mentor.WorkspaceId, SubmissionId = submission.SubmissionId,
                StrengthsMarkdown = "Mentor-private draft", Version = 1,
                CreatedAtUtc = App.Clock.GetUtcNow().UtcDateTime, UpdatedAtUtc = App.Clock.GetUtcNow().UtcDateTime
            });
            return await d.SaveChangesAsync();
        });
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, null), default)).ShouldSucceed();
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 1), default)).ShouldSucceed();
        (await App.Db(d => d.ReviewDrafts.AnyAsync(x => x.Id == draftId))).ShouldBeTrue();
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 2), default)).ShouldSucceed();

        App.Clock.Advance(TimeSpan.FromMinutes(1));
        var mentorships = App.Get<IMentorshipService>();
        (await mentorships.ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default)).ShouldSucceed();
        var closed = (await Consent.GetAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed();
        closed.Version.ShouldBe(4);
        closed.RevokedAtUtc.ShouldBe(App.Clock.GetUtcNow().UtcDateTime);
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(closed);
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 4), default)).ShouldBeNotFound();
        (await Consent.RevokeAsync(mentee.UserId, new(mentee.MentorshipId, 4), default)).ShouldBeNotFound();
        (await mentorships.ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Closed, default)).ShouldSucceed();
        (await Consent.GetAsync(mentor.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(closed);
        (await mentorships.ChangeStatusAsync(mentor.UserId, mentee.MentorshipId, MentorshipStatus.Active, default)).ShouldSucceed();
        (await Consent.GetAsync(mentee.UserId, mentee.MentorshipId, default)).ShouldSucceed().ShouldBe(closed);
        (await App.Db(d => d.ReviewDrafts.SingleAsync(x => x.Id == draftId))).StrengthsMarkdown.ShouldBe("Mentor-private draft");
        (await Consent.GrantAsync(mentee.UserId, new(mentee.MentorshipId, Options.DisclosureVersion, 4), default))
            .ShouldSucceed().Version.ShouldBe(5);
    }

    [Theory]
    [InlineData(false, "", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(true, "Approved provider notice", true)]
    public void Options_validate_a_required_notice_only_when_enabled(bool enabled, string notice, bool valid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Reviews:Enabled"] = enabled.ToString(),
            ["Mcp:Reviews:ProviderNotice"] = notice
        }).Build();
        var services = new ServiceCollection().AddMentorshipsModule(configuration);
        using var provider = services.BuildServiceProvider();
        if (valid)
        {
            provider.GetRequiredService<IStartupValidator>().Validate();
            provider.GetRequiredService<IOptions<McpReviewOptions>>().Value.Enabled.ShouldBe(enabled);
        }
        else
            Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Options_reject_a_provider_notice_over_the_existing_markdown_limit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:Reviews:Enabled"] = "true",
            ["Mcp:Reviews:ProviderNotice"] = new string('x', InputLimits.Markdown + 1)
        }).Build();
        using var provider = new ServiceCollection().AddMentorshipsModule(configuration).BuildServiceProvider();
        Should.Throw<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<McpReviewOptions>>().Value);
    }

    [Fact]
    public void Disclosure_version_is_a_bounded_lowercase_sha256_of_the_exact_utf8_notice()
    {
        const string notice = "Załączniki i historia — dostawca testowy.";
        var options = new McpReviewOptions { ProviderNotice = notice };
        options.DisclosureVersion.ShouldBe("mcp-review-v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(notice))));
        options.DisclosureVersion.Length.ShouldBeLessThanOrEqualTo(100);
        (new McpReviewOptions { ProviderNotice = notice + " " }).DisclosureVersion.ShouldNotBe(options.DisclosureVersion);
        McpReviewOptions.DefaultHistoryPageSize.ShouldBe(20);
        McpReviewOptions.MaxHistoryPageSize.ShouldBe(100);
        McpReviewOptions.MaxAttachmentChunkBytes.ShouldBe(65_536);
    }

    private void SetOptions(McpReviewOptions options)
    {
        var cache = App.Get<IOptionsMonitorCache<McpReviewOptions>>();
        cache.TryRemove(Microsoft.Extensions.Options.Options.DefaultName);
        cache.TryAdd(Microsoft.Extensions.Options.Options.DefaultName, options).ShouldBeTrue();
    }
}
