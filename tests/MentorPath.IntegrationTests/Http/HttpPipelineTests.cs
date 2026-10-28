using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MentorPath.Application.Feedback.Contracts;
using MentorPath.Application.Submissions.Contracts;
using MentorPath.Data.Enums;
using MentorPath.IntegrationTests.Infrastructure;

namespace MentorPath.IntegrationTests.Http;

public sealed class HttpPipelineTests(PostgresFixture db) : IntegrationTestBase(db)
{
    private WebAppFactory _factory = default!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _factory = new WebAppFactory(Database);
    }

    private static MultipartFormDataContent PracticalForm(Guid clientRequestId, byte[]? file = null, string fileName = "adr.pdf", string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(clientRequestId.ToString()), "clientRequestId" },
            { new StringContent("Uzasadnienie implementacji"), "rationaleMarkdown" },
            { new StringContent("4"), "confidenceBefore" },
            { new StringContent("https://github.com/demo/repo"), "externalUrl" },
        };
        if (file is not null)
        {
            var content = new ByteArrayContent(file);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(content, "file", fileName);
        }
        return form;
    }

    [Fact]
    public async Task Login_page_has_no_inline_scripts()
    {
        using var client = _factory.Client();
        var response = await client.GetAsync("/Account/Login");
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        var inline = System.Text.RegularExpressions.Regex.Matches(html, @"<script(?![^>]*\ssrc=)[^>]*>")
            .Select(m => m.Value).ToList();
        // Only the nonce-protected import map may be inline.
        inline.ShouldAllBe(tag => tag.Contains("type=\"importmap\"") && tag.Contains("nonce="));
        csp.ShouldContain("'nonce-");
    }

    [Fact]
    public async Task Health_endpoints_and_security_headers()
    {
        using var client = _factory.Client();
        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await client.GetAsync("/Account/Login");
        page.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        page.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        var contentSecurityPolicy = page.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();
        contentSecurityPolicy.ShouldContain("frame-ancestors 'none'");
        contentSecurityPolicy.ShouldContain("script-src 'self'");
        contentSecurityPolicy.ShouldContain("frame-src https://www.youtube-nocookie.com https://player.vimeo.com");
    }

    [Fact]
    public async Task Anonymous_users_are_redirected_or_rejected()
    {
        using var client = _factory.Client();
        var mentorPage = await client.GetAsync("/mentor");
        mentorPage.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        mentorPage.Headers.Location!.ToString().ShouldContain("/Account/Login");
        (await client.GetAsync($"/api/attachments/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/Account/Register")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Auth_cookie_is_http_only()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        using var client = _factory.Client();
        var page = await client.GetStringAsync("/Account/Login");
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token), ["_handler"] = "login",
            ["Input.Email"] = mentee.Email, ["Input.Password"] = TestDataBuilder.Password,
        }));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("mentorpath.auth=", StringComparison.Ordinal));
        cookie.ToLowerInvariant().ShouldContain("httponly");
        cookie.ToLowerInvariant().ShouldContain("samesite=lax");
    }

    [Fact]
    public async Task Upload_requires_antiforgery_and_download_is_restricted_to_authorized_users()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentorA);
        var outsider = await Data.MenteeAsync(mentorB);
        var asg = await Data.PublishedAssignmentAsync(mentorA, mentee, ExerciseType.PracticalTask);
        var pdf = "%PDF-1.7\nhello"u8.ToArray();

        using var menteeClient = _factory.Client();
        await WebAppFactory.LoginAsync(menteeClient, mentee.Email, TestDataBuilder.Password);

        (await menteeClient.PostAsync($"/api/assignments/{asg.AssignmentId}/practical-submissions", PracticalForm(Guid.NewGuid(), pdf)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var (header, token) = await WebAppFactory.AntiforgeryAsync(menteeClient);
        menteeClient.DefaultRequestHeaders.Add(header, token);
        var requestId = Guid.NewGuid();
        var ok = await menteeClient.PostAsync($"/api/assignments/{asg.AssignmentId}/practical-submissions", PracticalForm(requestId, pdf));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var dto = (await ok.Content.ReadFromJsonAsync<SubmissionResultDto>())!;
        dto.Status.ShouldBe(SubmissionStatus.PendingReview);

        var dup = await menteeClient.PostAsync($"/api/assignments/{asg.AssignmentId}/practical-submissions", PracticalForm(requestId, pdf));
        (await dup.Content.ReadFromJsonAsync<SubmissionResultDto>())!.WasDuplicate.ShouldBeTrue();

        var fake = await menteeClient.PostAsync($"/api/assignments/{asg.AssignmentId}/practical-submissions", PracticalForm(Guid.NewGuid(), "nope"u8.ToArray(), "x.png", "image/png"));
        fake.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var history = (await App.Get<ISubmissionService>().GetHistoryAsync(mentee.UserId, asg.AssignmentId, default)).ShouldSucceed();
        var attachmentId = history.Attempts.Single().Attachment!.AttachmentId;

        var download = await menteeClient.GetAsync($"/api/attachments/{attachmentId}");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(pdf);

        using var mentorAClient = _factory.Client();
        await WebAppFactory.LoginAsync(mentorAClient, mentorA.Email, TestDataBuilder.Password);
        (await mentorAClient.GetAsync($"/api/attachments/{attachmentId}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var mentorBClient = _factory.Client();
        await WebAppFactory.LoginAsync(mentorBClient, mentorB.Email, TestDataBuilder.Password);
        (await mentorBClient.GetAsync($"/api/attachments/{attachmentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var outsiderClient = _factory.Client();
        await WebAppFactory.LoginAsync(outsiderClient, outsider.Email, TestDataBuilder.Password);
        (await outsiderClient.GetAsync($"/api/attachments/{attachmentId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var (h2, t2) = await WebAppFactory.AntiforgeryAsync(outsiderClient);
        outsiderClient.DefaultRequestHeaders.Add(h2, t2);
        (await outsiderClient.PostAsync($"/api/assignments/{asg.AssignmentId}/practical-submissions", PracticalForm(Guid.NewGuid())))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Mentor_pages_do_not_render_foreign_workspace_data()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentorA);
        var asg = await Data.PublishedAssignmentAsync(mentorA, mentee, ExerciseType.QuizSingleChoice);

        using var mentorBClient = _factory.Client();
        await WebAppFactory.LoginAsync(mentorBClient, mentorB.Email, TestDataBuilder.Password);
        foreach (var url in new[] { $"/mentor/mentees/{mentee.MentorshipId}", $"/mentor/mentees/{mentee.MentorshipId}/roadmap",
                     $"/mentor/catalog/topics/{asg.TopicId}", $"/mentor/exercises/{asg.ExerciseId}/edit" })
        {
            var html = await mentorBClient.GetStringAsync(url);
            html.ShouldContain("Nie znaleziono", customMessage: url);
            html.ShouldNotContain("Scenariusz quizu", customMessage: url);
            html.ShouldNotContain(mentee.Email, customMessage: url);
        }

        using var menteeClient = _factory.Client();
        await WebAppFactory.LoginAsync(menteeClient, mentee.Email, TestDataBuilder.Password);
        var assignmentHtml = await menteeClient.GetStringAsync($"/app/assignments/{asg.AssignmentId}");
        assignmentHtml.ShouldContain("Scenariusz quizu");
        assignmentHtml.ShouldNotContain("Guide quizu");
        assignmentHtml.ShouldNotContain("Wyja&#x15B;nienie");
        (await menteeClient.GetAsync("/mentor")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Feedback_entry_is_available_to_both_roles_but_notes_are_not_returned_by_web_routes()
    {
        var mentor = await Data.MentorAsync();
        var mentee = await Data.MenteeAsync(mentor);
        const string privateNote = "<script>window.__feedbackPwned=true</script>";
        (await App.Get<IProductFeedbackService>().SubmitAsync(mentor.UserId,
            new SubmitProductFeedbackRequest(privateNote, "/mentor"), default)).ShouldSucceed();

        using var mentorClient = _factory.Client();
        await WebAppFactory.LoginAsync(mentorClient, mentor.Email, TestDataBuilder.Password);
        foreach (var url in new[] { "/mentor", "/mentor/feedback" })
        {
            var html = await mentorClient.GetStringAsync(url);
            html.ShouldContain("Uwaga do produktu", customMessage: url);
            html.ShouldContain("mentor/feedback", customMessage: url);
            html.ShouldNotContain(privateNote, customMessage: url);
        }
        (await mentorClient.GetAsync("/mentor/feedback/list")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await mentorClient.GetAsync("/api/product-feedback")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var menteeClient = _factory.Client();
        await WebAppFactory.LoginAsync(menteeClient, mentee.Email, TestDataBuilder.Password);
        foreach (var url in new[] { "/app", "/app/feedback" })
        {
            var html = await menteeClient.GetStringAsync(url);
            html.ShouldContain("Uwaga do produktu", customMessage: url);
            html.ShouldContain("app/feedback", customMessage: url);
            html.ShouldNotContain(privateNote, customMessage: url);
        }
        (await menteeClient.GetAsync("/app/feedback/list")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await menteeClient.GetAsync("/api/product-feedback")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await App.Get<IProductFeedbackOperatorService>().ListAsync(500, default))
            .ShouldContain(x => x.AuthorUserId == mentor.UserId && x.Message == privateNote);
    }

    [Fact]
    public async Task Mentee_with_multiple_mentorships_is_asked_to_choose_after_login()
    {
        var mentorA = await Data.MentorAsync();
        var mentorB = await Data.MentorAsync();
        var single = await Data.MenteeAsync(mentorA);
        var multi = await Data.MenteeAsync(mentorA);
        await Data.MenteeAsync(mentorB, multi.Email, multi.UserId);

        using var multiClient = _factory.Client();
        await WebAppFactory.LoginAsync(multiClient, multi.Email, TestDataBuilder.Password);
        (await multiClient.GetAsync("/")).Headers.Location!.ToString().ShouldEndWith("/app/mentorships");

        using var singleClient = _factory.Client();
        await WebAppFactory.LoginAsync(singleClient, single.Email, TestDataBuilder.Password);
        (await singleClient.GetAsync("/")).Headers.Location!.ToString().ShouldEndWith("/app");
    }

    public override async Task DisposeAsyncCore() => await _factory.DisposeAsync();
}
