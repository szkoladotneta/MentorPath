using System.Text.Json;
using MentorPath.Data;
using MentorPath.IntegrationTests.Infrastructure;
using MentorPath.Web.Cli;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.IntegrationTests.Privacy;

public sealed class PrivacyCommandTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private static Dictionary<string, string> Options(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    private async Task<(int Code, string Out, string Err)> RunAsync(string command, Dictionary<string, string> options)
    {
        await using var scope = App.CreateScope();
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = command == "export-user"
            ? await PrivacyCommands.ExportAsync(scope.ServiceProvider, options, stdout, stderr, default)
            : await PrivacyCommands.AnonymizeAsync(scope.ServiceProvider, options, stdout, stderr, default);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"mentorpath-export-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Export_user_writes_json_only_to_a_new_private_file()
    {
        var s = await PersonalDataTests.BuildAsync(App);
        var path = TempPath();
        try
        {
            var (code, stdout, stderr) = await RunAsync("export-user", Options(("email", s.A.Email), ("out", path)));

            code.ShouldBe(0, stderr);
            stdout.ShouldContain($"UserId={s.A.UserId}");
            stdout.ShouldContain("Submissions=3");
            stdout.ShouldNotContain(s.A.Email, Case.Insensitive);
            stdout.ShouldNotContain("sekret");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            json.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
            json.RootElement.GetProperty("profile").GetProperty("email").GetString().ShouldBe(s.A.Email);
            json.RootElement.GetProperty("submissions")[2].GetProperty("status").GetString().ShouldBe("PendingReview");
            json.RootElement.GetProperty("generatedAtUtc").GetString().ShouldEndWith("Z");
            if (!OperatingSystem.IsWindows())
                File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Export_user_refuses_to_overwrite_an_existing_file()
    {
        var s = await PersonalDataTests.BuildAsync(App);
        var path = TempPath();
        await File.WriteAllTextAsync(path, "keep");
        try
        {
            var (code, _, stderr) = await RunAsync("export-user", Options(("user-id", s.A.UserId.ToString()), ("out", path)));

            code.ShouldBe(1);
            stderr.ShouldContain("already exists");
            (await File.ReadAllTextAsync(path)).ShouldBe("keep");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("export-user", "")]
    [InlineData("export-user", "email")]
    [InlineData("export-user", "email,user-id,out")]
    [InlineData("export-user", "bad-id,out")]
    [InlineData("anonymize-user", "")]
    [InlineData("anonymize-user", "email,user-id")]
    [InlineData("anonymize-user", "bad-id")]
    public async Task Commands_print_usage_and_exit_2_for_invalid_arguments(string command, string present)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in present.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (key == "email") options["email"] = "someone@test.local";
            if (key == "user-id") options["user-id"] = Guid.NewGuid().ToString();
            if (key == "bad-id") options["user-id"] = "not-a-guid";
            if (key == "out") options["out"] = TempPath();
        }

        var (code, stdout, stderr) = await RunAsync(command, options);

        code.ShouldBe(2);
        stderr.ShouldStartWith("Usage:");
        stdout.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_user_exits_1_without_echoing_the_email()
    {
        var email = $"ghost-{Guid.NewGuid():N}"[..14] + "@test.local";
        var path = TempPath();

        var export = await RunAsync("export-user", Options(("email", email), ("out", path)));
        var anonymize = await RunAsync("anonymize-user", Options(("email", email), ("confirm", "true")));

        export.Code.ShouldBe(1);
        anonymize.Code.ShouldBe(1);
        (export.Err + anonymize.Err).ShouldContain("privacy.not_found");
        (export.Out + export.Err + anonymize.Out + anonymize.Err).ShouldNotContain(email, Case.Insensitive);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Anonymize_user_is_a_dry_run_unless_confirmed()
    {
        var s = await PersonalDataTests.BuildAsync(App);
        var before = await PersonalDataTests.SubjectFullSnapshotAsync(App, s.A);

        var dry = await RunAsync("anonymize-user", Options(("email", s.A.Email)));

        dry.Code.ShouldBe(0, dry.Err);
        dry.Out.ShouldContain("Dry run");
        dry.Out.ShouldContain("SubmissionsScrubbed=3");
        (await PersonalDataTests.SubjectFullSnapshotAsync(App, s.A)).ShouldBe(before);

        var applied = await RunAsync("anonymize-user", Options(("email", s.A.Email), ("confirm", "true")));

        applied.Code.ShouldBe(0, applied.Err);
        applied.Out.ShouldStartWith("Anonymized.");
        applied.Out.ShouldContain($"UserId={s.A.UserId}");
        (applied.Out + dry.Out).ShouldNotContain(s.A.Email, Case.Insensitive);
        (await App.Db(db => db.Users.SingleAsync(u => u.Id == s.A.UserId))).Email.ShouldBe($"deleted-{s.A.UserId:N}@anonymized.invalid");
    }

    [Fact]
    public void Privacy_commands_are_registered_in_the_cli()
    {
        CliRunner.IsCliCommand(["export-user"]).ShouldBeTrue();
        CliRunner.IsCliCommand(["anonymize-user"]).ShouldBeTrue();
    }
}
