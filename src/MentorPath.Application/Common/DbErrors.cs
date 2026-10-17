using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MentorPath.Application.Common;

internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex, string? constraintName = null) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraintName is null || string.Equals(pg.ConstraintName, constraintName, StringComparison.Ordinal));

    public static string? ConstraintName(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName;
}
