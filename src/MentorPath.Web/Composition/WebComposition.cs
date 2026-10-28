using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Azure.Identity;
using MentorPath.Application.Common;
using MentorPath.Data;
using MentorPath.Data.Enums;
using MentorPath.Web.BackgroundJobs;
using MentorPath.Web.Components;
using MentorPath.Web.Components.Account;
using MentorPath.Web.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MentorPath.Web.Composition;

public static class WebComposition
{
    public const string UploadsRateLimit = "uploads";
    public const string CspNonceKey = "csp-nonce";

    public static void AddMentorPathWeb(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole();
        }

        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddScoped<IdentityRedirectManager>();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        services.AddMentorPathData(configuration);
        services.AddMentorPathIdentity(builder.Environment);
        services.AddMentorPathModules(configuration);
        services.AddOptions<BackgroundJobOptions>()
            .Bind(configuration.GetSection(BackgroundJobOptions.SectionName))
            .Validate(options => options.IntervalSeconds > 0, "BackgroundJobs:IntervalSeconds must be greater than zero.")
            .ValidateOnStart();
        services.AddSingleton<IPostgresAdvisoryLock>(_ =>
            new PostgresAdvisoryLock(
                configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("Connection string 'Default' is not configured (ConnectionStrings__Default)."),
                PostgresAdvisoryLock.SchedulerLockKey));
        services.AddHostedService<BackgroundJobWorker>();
        if (builder.Environment.IsProduction() && configuration["Storage:Provider"] == "InMemory")
            throw new InvalidOperationException("InMemory storage is not allowed in Production.");

        AddKeyRing(services, configuration, builder.Environment);

        services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var path = ctx.Request.Path.Value ?? "";
                var sensitive = HttpMethods.IsPost(ctx.Request.Method)
                    && (path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/invite/", StringComparison.OrdinalIgnoreCase));
                if (!sensitive) return RateLimitPartition.GetNoLimiter("none");
                var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"auth:{key}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                });
            });
            o.AddPolicy(UploadsRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        if (configuration.GetValue<bool>("App:BehindProxy"))
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
            });
        }
    }

    private static void AddKeyRing(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var keysPath = configuration["DataProtection:KeysPath"];
        var keysBlobUri = configuration["DataProtection:BlobUri"];
        var certificatePath = configuration["DataProtection:CertificatePath"];
        var builder = services.AddDataProtection().SetApplicationName("MentorPath");
        if (!string.IsNullOrWhiteSpace(keysBlobUri))
        {
            if (!Uri.TryCreate(keysBlobUri, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query))
                throw new InvalidOperationException("DataProtection:BlobUri must be an HTTPS blob URI without a SAS token.");
            builder.PersistKeysToAzureBlobStorage(uri, new DefaultAzureCredential());
        }
        else if (!string.IsNullOrWhiteSpace(keysPath))
            builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        if (string.IsNullOrWhiteSpace(certificatePath) && environment.IsProduction())
            throw new InvalidOperationException("DataProtection:CertificatePath is required in Production to encrypt the key ring.");

        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var password = configuration["DataProtection:CertificatePassword"];
            builder.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password));
        }
    }

    public static IServiceCollection AddMentorPathData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured (ConnectionStrings__Default).");
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }

    public static IServiceCollection AddMentorPathIdentity(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(o =>
        {
            o.LoginPath = "/Account/Login";
            o.AccessDeniedPath = "/Account/AccessDenied";
            o.Cookie.Name = "mentorpath.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx => ApiAware(ctx, StatusCodes.Status401Unauthorized);
            o.Events.OnRedirectToAccessDenied = ctx => ApiAware(ctx, StatusCodes.Status403Forbidden);
        });

        services.AddIdentityCore<AppUser>(o =>
            {
                o.SignIn.RequireConfirmedAccount = false;
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<PolishIdentityErrorDescriber>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AppRoles.Mentor, p => p.RequireRole(AppRoles.Mentor))
            .AddPolicy(AppRoles.Mentee, p => p.RequireRole(AppRoles.Mentee));
        return services;
    }

    private static Task ApiAware(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    public static async Task UseMentorPathWebAsync(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("App:BehindProxy")) app.UseForwardedHeaders();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync();
            }
            await EnsureRolesAsync(scope.ServiceProvider);
            if (app.Configuration.GetValue("Storage:EnsureContainerOnStartup", app.Environment.IsDevelopment()))
                await MentorPathApplication.EnsureStorageAsync(scope.ServiceProvider, app.Lifetime.ApplicationStopping);
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.Use(async (ctx, next) =>
        {
            var h = ctx.Response.Headers;
            var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            ctx.Items[CspNonceKey] = nonce;
            h.XContentTypeOptions = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["X-Frame-Options"] = "DENY";
            h.ContentSecurityPolicy = $"frame-ancestors 'none'; object-src 'none'; base-uri 'self'; script-src 'self' 'nonce-{nonce}'; form-action 'self'; frame-src https://www.youtube-nocookie.com https://player.vimeo.com";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            await next();
        });

        app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/health"),
            branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
        if (!app.Configuration.GetValue<bool>("App:DisableHttpsRedirection")) app.UseHttpsRedirection();
        app.UseRateLimiter();
        app.UseAntiforgery();

        app.MapStaticAssets();
        // Our middleware sets a stricter frame-ancestors 'none' CSP, so Blazor's default 'self' header is disabled.
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);
        app.MapAdditionalIdentityEndpoints();
        app.MapAttachmentEndpoints();
        app.MapPracticalSubmissionEndpoints();
        app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
    }

    public static async Task EnsureRolesAsync(IServiceProvider sp)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
        }
    }
}
