using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Persistence;

internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 or 1555 } => true,
        // Npgsql:     PostgresException { SqlState: "23505" } => true,
        // SQL Server: SqlException { Number: 2601 or 2627 } => true,
        _ => false,
    };
}
