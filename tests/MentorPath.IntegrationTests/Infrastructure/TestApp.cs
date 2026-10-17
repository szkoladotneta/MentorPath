using MentorPath.Application.Common;
using MentorPath.Application.Common.Storage;
using MentorPath.Data;
using MentorPath.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MentorPath.IntegrationTests.Infrastructure;

/// <summary>Application services wired exactly like production, against the test database, with in-memory storage and fake AI.</summary>
public sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _root;

    private TestApp(ServiceProvider root, FakeTimeProvider clock, InMemoryFileStorage storage)
    {
        _root = root;
        Clock = clock;
        Storage = storage;
        Data = new TestDataBuilder(this);
    }

    public FakeTimeProvider Clock { get; }
    public InMemoryFileStorage Storage { get; }
    public TestDataBuilder Data { get; }

    public static async Task<TestApp> CreateAsync(PostgresFixture db, IDictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:PublicBaseUrl"] = "https://mentorpath.test",
                ["Ai:Provider"] = "Fake",
                ["Ai:FakeMode"] = "Ok",
                ["Ai:TimeoutSeconds"] = "2",
                ["Ai:MonthlyGenerationLimitPerWorkspace"] = "50",
            })
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(db.ConnectionString));
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddMentorPathModules(config);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
        var storage = new InMemoryFileStorage();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IFileStorage>(storage);

        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using (var scope = root.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in AppRoles.All)
            {
                if (!await roles.RoleExistsAsync(role))
                {
                    try { await roles.CreateAsync(new IdentityRole<Guid>(role)); }
                    catch (DbUpdateException) { /* created concurrently by another test */ }
                }
            }
        }
        return new TestApp(root, clock, storage);
    }

    /// <summary>Resolves a service in a fresh scope (mirrors one HTTP request / circuit operation).</summary>
    public T Get<T>() where T : notnull => _root.CreateScope().ServiceProvider.GetRequiredService<T>();

    public async Task<TResult> Db<TResult>(Func<AppDbContext, Task<TResult>> action)
    {
        await using var db = await _root.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await action(db);
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();
}

public static class ResultAssertions
{
    public static T ShouldSucceed<T>(this Result<T> result)
    {
        result.IsSuccess.ShouldBeTrue($"Expected success but got {result.Error?.Code}: {result.Error?.Message} {Fields(result.Error)}");
        return result.Value!;
    }

    public static void ShouldSucceed(this Result result) =>
        result.IsSuccess.ShouldBeTrue($"Expected success but got {result.Error?.Code}: {result.Error?.Message} {Fields(result.Error)}");

    public static AppError ShouldFail(this Result result, ErrorKind kind, string? code = null)
    {
        result.IsSuccess.ShouldBeFalse("Expected failure but the call succeeded.");
        result.Error!.Kind.ShouldBe(kind, $"code={result.Error.Code}");
        if (code is not null) result.Error.Code.ShouldBe(code);
        return result.Error;
    }

    public static void ShouldBeNotFound(this Result result) => result.ShouldFail(ErrorKind.NotFound);

    private static string Fields(AppError? e) => e?.FieldErrors is null ? "" : string.Join("; ", e.FieldErrors.Select(f => $"{f.Key}: {string.Join(" ", f.Value)}"));
}
