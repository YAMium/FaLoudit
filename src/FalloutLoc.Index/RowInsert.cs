using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace FalloutLoc.Index;

/// <summary>Single prepared INSERT on the active per-plugin transaction.
/// Reset retains bindings; callers rebind every nonconstant column, including NULL.</summary>
internal sealed class RowInsert : IDisposable
{
    private readonly sqlite3 _db;
    private readonly sqlite3_stmt _statement;

    public RowInsert(SqliteTransaction transaction, string sql)
    {
        _db = transaction.Connection?.Handle ?? throw new InvalidOperationException("An active transaction is required.");
        var rc = raw.sqlite3_prepare_v2(_db, sql, out var statement);
        if (rc != raw.SQLITE_OK)
        {
            statement?.Dispose();
            SqliteException.ThrowExceptionForRC(rc, _db);
        }
        _statement = statement ?? throw new InvalidOperationException("SQLite did not prepare an INSERT.");
    }

    public void Bind(int position, long value) => Check(raw.sqlite3_bind_int64(_statement, position, value));

    public void Bind(int position, string? value) => Check(value is null
        ? raw.sqlite3_bind_null(_statement, position)
        : raw.sqlite3_bind_text(_statement, position, value));

    public void Execute()
    {
        var rc = raw.sqlite3_step(_statement);
        if (rc != raw.SQLITE_DONE) SqliteException.ThrowExceptionForRC(rc, _db);
        Check(raw.sqlite3_reset(_statement));
    }

    private void Check(int rc)
    {
        if (rc != raw.SQLITE_OK) SqliteException.ThrowExceptionForRC(rc, _db);
    }

    public void Dispose() => _statement.Dispose();
}
