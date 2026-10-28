using Npgsql;

namespace MentorPath.Web.BackgroundJobs;

internal sealed class PostgresAdvisoryLock(string connectionString, long lockKey) : IPostgresAdvisoryLock
{
    public const long SchedulerLockKey = 0x4D50544842474A;

    public async ValueTask<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        var lockQueryStarted = false;
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lockKey)", connection);
            command.Parameters.AddWithValue("lockKey", lockKey);
            lockQueryStarted = true;
            var lockAcquired = (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (!lockAcquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new AdvisoryLockLease(connection, lockKey);
        }
        catch
        {
            if (lockQueryStarted)
                NpgsqlConnection.ClearPool(connection);
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class AdvisoryLockLease(NpgsqlConnection connection, long lockKey) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lockKey)", connection);
                command.Parameters.AddWithValue("lockKey", lockKey);
                var released = (bool)(await command.ExecuteScalarAsync(CancellationToken.None))!;
                if (!released)
                    throw new InvalidOperationException("The background job advisory lock was not held by its owning connection.");
            }
            catch
            {
                NpgsqlConnection.ClearPool(connection);
                throw;
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
