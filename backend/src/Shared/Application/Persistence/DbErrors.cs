using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Shared.Application.Persistence;

public static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 or 1555 } => true,
        _ => false,
    };
}
