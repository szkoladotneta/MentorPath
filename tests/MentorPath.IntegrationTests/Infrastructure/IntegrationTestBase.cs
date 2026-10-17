namespace MentorPath.IntegrationTests.Infrastructure;

[Collection(DatabaseCollection.Name)]
public abstract class IntegrationTestBase(PostgresFixture db) : IAsyncLifetime
{
    protected PostgresFixture Database { get; } = db;
    protected TestApp App { get; private set; } = default!;
    protected TestDataBuilder Data => App.Data;

    protected virtual IDictionary<string, string?>? Settings => null;

    public virtual async Task InitializeAsync() => App = await TestApp.CreateAsync(Database, Settings);

    public async Task DisposeAsync()
    {
        await DisposeAsyncCore();
        await App.DisposeAsync();
    }

    public virtual Task DisposeAsyncCore() => Task.CompletedTask;
}
