using Microsoft.Data.Sqlite;

namespace FalloutLoc.Index;

internal static class SqliteSchema
{
    public const int Version = 5;

    public static void Create(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;

            CREATE TABLE schema_info (
                version INTEGER NOT NULL
            );
            INSERT INTO schema_info(version) VALUES (5);

            CREATE TABLE snapshots (
                id INTEGER PRIMARY KEY,
                created_utc TEXT NOT NULL,
                mode TEXT NOT NULL,
                mo2_root TEXT NOT NULL,
                profile_name TEXT NOT NULL,
                source_language TEXT NOT NULL,
                target_language TEXT NOT NULL,
                load_order_fingerprint TEXT NOT NULL,
                backend_name TEXT NOT NULL,
                status TEXT NOT NULL
            );

            CREATE TABLE physical_providers (
                id INTEGER PRIMARY KEY,
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id),
                logical_path TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                source_name TEXT NOT NULL,
                effective_priority INTEGER NOT NULL,
                profile_line INTEGER,
                physical_path TEXT NOT NULL,
                is_winner INTEGER NOT NULL,
                file_length INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                sha256 TEXT
            );

            CREATE TABLE plugins (
                id INTEGER PRIMARY KEY,
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id),
                load_order_index INTEGER NOT NULL,
                name TEXT NOT NULL,
                physical_path TEXT NOT NULL,
                source_mod TEXT NOT NULL,
                effective_priority INTEGER NOT NULL,
                file_length INTEGER NOT NULL,
                last_write_utc TEXT NOT NULL,
                sha256 TEXT,
                parse_status TEXT NOT NULL,
                error TEXT,
                record_count INTEGER NOT NULL DEFAULT 0,
                coverage_gap_record_count INTEGER NOT NULL DEFAULT 0,
                string_count INTEGER NOT NULL DEFAULT 0,
                content_count INTEGER NOT NULL DEFAULT 0,
                encoding_class TEXT
            );

            CREATE TABLE records (
                id INTEGER PRIMARY KEY,
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id),
                plugin_id INTEGER NOT NULL REFERENCES plugins(id),
                form_key TEXT NOT NULL,
                origin_plugin TEXT NOT NULL,
                record_type TEXT NOT NULL,
                editor_id TEXT,
                is_deleted INTEGER NOT NULL,
                is_compressed INTEGER NOT NULL,
                parse_status TEXT NOT NULL,
                parse_warnings TEXT
            );

            CREATE TABLE strings (
                id INTEGER PRIMARY KEY,
                record_id INTEGER NOT NULL REFERENCES records(id),
                semantic_path TEXT NOT NULL,
                category TEXT NOT NULL,
                text TEXT,
                normalized_text TEXT,
                language TEXT NOT NULL,
                encoding_evidence TEXT NOT NULL,
                bytes_sha256 TEXT,
                ambiguous INTEGER NOT NULL
            );

            CREATE TABLE record_contents (
                id INTEGER PRIMARY KEY,
                record_id INTEGER NOT NULL REFERENCES records(id),
                semantic_path TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                text TEXT,
                normalized_text TEXT,
                encoding_evidence TEXT NOT NULL,
                bytes_sha256 TEXT,
                ambiguous INTEGER NOT NULL,
                is_heuristic INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public static void Finalize(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE UNIQUE INDEX ux_plugins_snapshot_order ON plugins(snapshot_id, load_order_index);
            CREATE INDEX ix_plugins_name ON plugins(name COLLATE NOCASE);
            CREATE INDEX ix_records_form_key ON records(snapshot_id, form_key COLLATE NOCASE);
            CREATE INDEX ix_records_editor_id ON records(snapshot_id, editor_id COLLATE NOCASE);
            CREATE INDEX ix_records_coverage ON records(snapshot_id, parse_status, record_type);
            CREATE INDEX ix_strings_record_id ON strings(record_id);
            CREATE INDEX ix_strings_normalized_text ON strings(normalized_text);
            CREATE INDEX ix_strings_diagnostics ON strings(language, category COLLATE NOCASE, record_id, semantic_path);
            CREATE INDEX ix_record_contents_record_id ON record_contents(record_id);
            CREATE INDEX ix_record_contents_normalized_text ON record_contents(normalized_text);
            CREATE INDEX ix_physical_logical ON physical_providers(snapshot_id, logical_path COLLATE NOCASE);
            ANALYZE;
            """;
        command.ExecuteNonQuery();
    }
}
