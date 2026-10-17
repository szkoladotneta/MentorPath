using MentorPath.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MentorPath.IntegrationTests.Infrastructure;

/// <summary>One PostgreSQL container per test assembly; migrations applied once. Tests isolate data by creating their own workspaces.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("mentorpath_tests")
        .WithUsername("mentorpath")
        .WithPassword("mentorpath")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "database";
}
