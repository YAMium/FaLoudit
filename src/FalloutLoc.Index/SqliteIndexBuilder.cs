using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FalloutLoc.Backends.Abstractions;
using FalloutLoc.Backends.Models;
using FalloutLoc.Core.IO;
using FalloutLoc.Index.Models;
using Microsoft.Data.Sqlite;

namespace FalloutLoc.Index;

public sealed class SqliteIndexBuilder(
    IWorkspaceFileSystem workspaceFileSystem,
    IPluginBackend backend,
    IPluginEncodingClassifier encodingClassifier)
{
    public const string IndexerCacheVersion = "7";

    public string CacheIdentity => $"{backend.Name}|schema={SqliteSchema.Version}|indexer={IndexerCacheVersion}";

    public IndexBuildResult Build(
        IndexBuildRequest request,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Plugins.Count == 0)
        {
            throw new ArgumentException("At least one plugin is required to build an index.", nameof(request));
        }

        var destination = workspaceFileSystem.PrepareFileDestination(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination)!;
        var staged = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.staged");
        workspaceFileSystem.PrepareFileDestination(staged);
        var stopwatch = Stopwatch.StartNew();
        var parsedPlugins = 0;
        var reusedPlugins = 0;
        var failedPlugins = 0;
        var partiallyParsedPlugins = 0;
        long coverageGapRecords = 0;
        long totalRecords = 0;
        long totalStrings = 0;
        long totalContents = 0;
        var clonedPrevious = CanClonePrevious(request);

        try
        {
            if (clonedPrevious)
            {
                workspaceFileSystem.CopyFileWithinWorkspace(request.PreviousDatabasePath!, staged);
            }

            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                // Opening the primary database through a URI enables SQLite URI handling for
                // ATTACH as well. This is required for mode=ro on the previous snapshot.
                DataSource = ToSqliteFileUri(staged, "rwc"),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString()))
            {
                connection.Open();
                long snapshotId;
                PluginWorkItem[] work;
                if (clonedPrevious)
                {
                    ConfigureConnection(connection);
                    snapshotId = ResetClonedSnapshot(connection, request);
                    ReplaceProviders(connection, snapshotId, request.PhysicalProviders);
                    work = PrepareClonedPlugins(connection, snapshotId, request.Plugins);
                }
                else
                {
                    SqliteSchema.Create(connection);
                    snapshotId = InsertSnapshot(connection, request);
                    InsertProviders(connection, snapshotId, request.PhysicalProviders);
                    work = request.Plugins.OrderBy(plugin => plugin.LoadOrderIndex)
                        .Select(plugin => new PluginWorkItem(plugin, InsertPlugin(connection, snapshotId, plugin)))
                        .ToArray();
                }

                foreach (var item in work.Where(item => item.Reuse is not null))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reuse = item.Reuse!;
                    totalRecords += reuse.RecordCount;
                    totalStrings += reuse.StringCount;
                    totalContents += reuse.ContentCount;
                    coverageGapRecords += reuse.CoverageGapRecordCount;
                    if (reuse.ParseStatus == "partiallyParsed")
                    {
                        partiallyParsedPlugins++;
                    }

                    reusedPlugins++;
                    item.Completed = true;

                    progress?.Report(new IndexProgress
                    {
                        CompletedPlugins = parsedPlugins + reusedPlugins + failedPlugins,
                        TotalPlugins = request.Plugins.Count,
                        PluginName = item.Plugin.Name,
                        ParseStatus = "reused",
                        TotalRecords = totalRecords,
                        TotalStrings = totalStrings,
                        TotalContents = totalContents,
                    });
                }

                foreach (var item in work.Where(item => !item.Completed))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parseStatus = "parsed";
                    try
                    {
                        var counts = IndexPlugin(
                            connection,
                            snapshotId,
                            item.PluginId,
                            request.Mode,
                            item.Plugin,
                            cancellationToken);
                        totalRecords += counts.Records;
                        totalStrings += counts.Strings;
                        totalContents += counts.Contents;
                        coverageGapRecords += counts.CoverageGapRecords;
                        if (counts.CoverageGapRecords > 0)
                        {
                            parseStatus = "partiallyParsed";
                            partiallyParsedPlugins++;
                        }

                        parsedPlugins++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        parseStatus = "failed";
                        failedPlugins++;
                        MarkPluginFailed(connection, item.PluginId, exception);
                    }

                    progress?.Report(new IndexProgress
                    {
                        CompletedPlugins = parsedPlugins + reusedPlugins + failedPlugins,
                        TotalPlugins = request.Plugins.Count,
                        PluginName = item.Plugin.Name,
                        ParseStatus = parseStatus,
                        TotalRecords = totalRecords,
                        TotalStrings = totalStrings,
                        TotalContents = totalContents,
                    });
                }

                MarkSnapshotComplete(connection, snapshotId);
                if (clonedPrevious)
                {
                    using var optimize = connection.CreateCommand();
                    optimize.CommandText = "PRAGMA optimize;";
                    optimize.ExecuteNonQuery();
                }
                else
                {
                    SqliteSchema.Finalize(connection);
                }
            }

            workspaceFileSystem.ReplaceFileAtomic(staged, destination);
            stopwatch.Stop();
            return new IndexBuildResult
            {
                DatabasePath = destination,
                SchemaVersion = SqliteSchema.Version,
                ParsedPlugins = parsedPlugins,
                ReusedPlugins = reusedPlugins,
                FailedPlugins = failedPlugins,
                PartiallyParsedPlugins = partiallyParsedPlugins,
                CoverageGapRecords = coverageGapRecords,
                Records = totalRecords,
                Strings = totalStrings,
                Contents = totalContents,
                Duration = stopwatch.Elapsed,
            };
        }
        finally
        {
            workspaceFileSystem.DeleteFileIfExists(staged);
            workspaceFileSystem.DeleteFileIfExists(staged + "-journal");
            workspaceFileSystem.DeleteFileIfExists(staged + "-wal");
            workspaceFileSystem.DeleteFileIfExists(staged + "-shm");
        }
    }

    private long InsertSnapshot(SqliteConnection connection, IndexBuildRequest request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO snapshots(created_utc, mode, mo2_root, profile_name, source_language, target_language,
                                  load_order_fingerprint, backend_name, status)
            VALUES ($created, $mode, $root, $profile, $sourceLanguage, $targetLanguage,
                    $fingerprint, $backend, 'building');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$mode", request.Mode.ToString());
        command.Parameters.AddWithValue("$root", request.Mo2Root);
        command.Parameters.AddWithValue("$profile", request.ProfileName);
        command.Parameters.AddWithValue("$sourceLanguage", request.SourceLanguage);
        command.Parameters.AddWithValue("$targetLanguage", request.TargetLanguage);
        command.Parameters.AddWithValue("$fingerprint", request.LoadOrderFingerprint);
        command.Parameters.AddWithValue("$backend", CacheIdentity);
        return (long)command.ExecuteScalar()!;
    }

    private static void InsertProviders(
        SqliteConnection connection,
        long snapshotId,
        IReadOnlyList<IndexPhysicalProviderInput> providers)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO physical_providers(
                snapshot_id, logical_path, source_kind, source_name, effective_priority,
                profile_line, physical_path, is_winner, file_length, last_write_utc, sha256)
            VALUES ($snapshot, $logical, $kind, $source, $priority, $line, $path, $winner, $length, $write, $sha);
            """;
        var snapshot = command.Parameters.Add("$snapshot", SqliteType.Integer);
        var logical = command.Parameters.Add("$logical", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var priority = command.Parameters.Add("$priority", SqliteType.Integer);
        var line = command.Parameters.Add("$line", SqliteType.Integer);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var winner = command.Parameters.Add("$winner", SqliteType.Integer);
        var length = command.Parameters.Add("$length", SqliteType.Integer);
        var write = command.Parameters.Add("$write", SqliteType.Text);
        var sha = command.Parameters.Add("$sha", SqliteType.Text);

        foreach (var provider in providers)
        {
            snapshot.Value = snapshotId;
            logical.Value = provider.LogicalPath;
            kind.Value = provider.SourceKind;
            source.Value = provider.SourceName;
            priority.Value = provider.EffectivePriority;
            line.Value = provider.ProfileLine is null ? DBNull.Value : provider.ProfileLine.Value;
            path.Value = provider.PhysicalPath;
            winner.Value = provider.IsWinner ? 1 : 0;
            length.Value = provider.FileLength;
            write.Value = provider.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture);
            sha.Value = provider.Sha256 is null ? DBNull.Value : provider.Sha256;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static long InsertPlugin(SqliteConnection connection, long snapshotId, IndexPluginInput plugin)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO plugins(
                snapshot_id, load_order_index, name, physical_path, source_mod, effective_priority,
                file_length, last_write_utc, sha256, parse_status)
            VALUES ($snapshot, $order, $name, $path, $source, $priority, $length, $write, $sha, 'building');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$snapshot", snapshotId);
        command.Parameters.AddWithValue("$order", plugin.LoadOrderIndex);
        command.Parameters.AddWithValue("$name", plugin.Name);
        command.Parameters.AddWithValue("$path", plugin.PhysicalPath);
        command.Parameters.AddWithValue("$source", plugin.SourceMod);
        command.Parameters.AddWithValue("$priority", plugin.EffectivePriority);
        command.Parameters.AddWithValue("$length", plugin.FileLength);
        command.Parameters.AddWithValue("$write", plugin.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sha", plugin.Sha256 is null ? DBNull.Value : plugin.Sha256);
        return (long)command.ExecuteScalar()!;
    }

    private bool CanClonePrevious(IndexBuildRequest request)
    {
        if (!request.ReuseUnchangedPlugins || string.IsNullOrWhiteSpace(request.PreviousDatabasePath))
        {
            return false;
        }

        var previous = Path.GetFullPath(request.PreviousDatabasePath);
        if (!File.Exists(previous))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = previous,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var validate = connection.CreateCommand();
            validate.CommandText = """
                SELECT (SELECT version FROM schema_info LIMIT 1), mode, source_language, target_language,
                       backend_name, status
                FROM snapshots
                ORDER BY id DESC LIMIT 1;
                """;
            using var reader = validate.ExecuteReader();
            return reader.Read()
                && reader.GetInt32(0) == SqliteSchema.Version
                && reader.GetString(1) == request.Mode.ToString()
                && reader.GetString(2) == request.SourceLanguage
                && reader.GetString(3) == request.TargetLanguage
                && reader.GetString(4) == CacheIdentity
                && reader.GetString(5) == "complete";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static string ToSqliteFileUri(string path, string mode) =>
        $"{new Uri(Path.GetFullPath(path)).AbsoluteUri}?mode={mode}&cache=private";

    private static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = DELETE;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            """;
        command.ExecuteNonQuery();
    }

    private long ResetClonedSnapshot(SqliteConnection connection, IndexBuildRequest request)
    {
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id FROM snapshots ORDER BY id DESC LIMIT 1;";
        var snapshotId = (long)(select.ExecuteScalar()
            ?? throw new InvalidDataException("Compatible cache has no snapshot."));

        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE snapshots
            SET created_utc = $created, mode = $mode, mo2_root = $root, profile_name = $profile,
                source_language = $sourceLanguage, target_language = $targetLanguage,
                load_order_fingerprint = $fingerprint, backend_name = $backend, status = 'building'
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$mode", request.Mode.ToString());
        update.Parameters.AddWithValue("$root", request.Mo2Root);
        update.Parameters.AddWithValue("$profile", request.ProfileName);
        update.Parameters.AddWithValue("$sourceLanguage", request.SourceLanguage);
        update.Parameters.AddWithValue("$targetLanguage", request.TargetLanguage);
        update.Parameters.AddWithValue("$fingerprint", request.LoadOrderFingerprint);
        update.Parameters.AddWithValue("$backend", CacheIdentity);
        update.Parameters.AddWithValue("$id", snapshotId);
        update.ExecuteNonQuery();
        return snapshotId;
    }

    private static void ReplaceProviders(
        SqliteConnection connection,
        long snapshotId,
        IReadOnlyList<IndexPhysicalProviderInput> providers)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM physical_providers WHERE snapshot_id = $snapshot;";
            delete.Parameters.AddWithValue("$snapshot", snapshotId);
            delete.ExecuteNonQuery();
        }

        InsertProviders(connection, snapshotId, providers);
    }

    private static PluginWorkItem[] PrepareClonedPlugins(
        SqliteConnection connection,
        long snapshotId,
        IReadOnlyList<IndexPluginInput> plugins)
    {
        var existing = ReadExistingPlugins(connection, snapshotId);
        var requestedNames = plugins.Select(plugin => plugin.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var transaction = connection.BeginTransaction();

        using (var freeLoadOrder = connection.CreateCommand())
        {
            freeLoadOrder.Transaction = transaction;
            freeLoadOrder.CommandText = "UPDATE plugins SET load_order_index = -id WHERE snapshot_id = $snapshot;";
            freeLoadOrder.Parameters.AddWithValue("$snapshot", snapshotId);
            freeLoadOrder.ExecuteNonQuery();
        }

        foreach (var cached in existing.Values.Where(cached => !requestedNames.Contains(cached.Name)))
        {
            DeletePluginData(connection, transaction, cached.PluginId);
            DeletePlugin(connection, transaction, cached.PluginId);
        }

        var work = new List<PluginWorkItem>(plugins.Count);
        foreach (var plugin in plugins.OrderBy(plugin => plugin.LoadOrderIndex))
        {
            if (existing.TryGetValue(plugin.Name, out var cached))
            {
                var reusable = cached.IsReusable(plugin)
                    ? new ReusablePlugin(
                        cached.PluginId,
                        cached.ParseStatus,
                        cached.RecordCount,
                        cached.CoverageGapRecordCount,
                        cached.StringCount,
                        cached.ContentCount,
                        cached.EncodingClass)
                    : null;
                if (reusable is null)
                {
                    DeletePluginData(connection, transaction, cached.PluginId);
                }

                UpdatePlugin(connection, transaction, cached.PluginId, plugin, reusable);
                work.Add(new PluginWorkItem(plugin, cached.PluginId) { Reuse = reusable });
            }
            else
            {
                work.Add(new PluginWorkItem(plugin, InsertPlugin(connection, transaction, snapshotId, plugin)));
            }
        }

        transaction.Commit();
        return work.ToArray();
    }

    private static Dictionary<string, ExistingPlugin> ReadExistingPlugins(SqliteConnection connection, long snapshotId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, physical_path, file_length, last_write_utc, sha256,
                   parse_status, record_count, coverage_gap_record_count, string_count, content_count, encoding_class
            FROM plugins WHERE snapshot_id = $snapshot;
            """;
        command.Parameters.AddWithValue("$snapshot", snapshotId);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, ExistingPlugin>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var item = new ExistingPlugin(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10),
                reader.IsDBNull(11) ? null : reader.GetString(11));
            result.Add(item.Name, item);
        }

        return result;
    }

    private static void DeletePluginData(SqliteConnection connection, SqliteTransaction transaction, long pluginId)
    {
        using var contents = connection.CreateCommand();
        contents.Transaction = transaction;
        contents.CommandText = "DELETE FROM record_contents WHERE record_id IN (SELECT id FROM records WHERE plugin_id = $id);";
        contents.Parameters.AddWithValue("$id", pluginId);
        contents.ExecuteNonQuery();

        using var strings = connection.CreateCommand();
        strings.Transaction = transaction;
        strings.CommandText = "DELETE FROM strings WHERE record_id IN (SELECT id FROM records WHERE plugin_id = $id);";
        strings.Parameters.AddWithValue("$id", pluginId);
        strings.ExecuteNonQuery();

        using var records = connection.CreateCommand();
        records.Transaction = transaction;
        records.CommandText = "DELETE FROM records WHERE plugin_id = $id;";
        records.Parameters.AddWithValue("$id", pluginId);
        records.ExecuteNonQuery();
    }

    private static void DeletePlugin(SqliteConnection connection, SqliteTransaction transaction, long pluginId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM plugins WHERE id = $id;";
        command.Parameters.AddWithValue("$id", pluginId);
        command.ExecuteNonQuery();
    }

    private static void UpdatePlugin(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long pluginId,
        IndexPluginInput plugin,
        ReusablePlugin? reuse)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE plugins
            SET load_order_index = $order, name = $name, physical_path = $path, source_mod = $source,
                effective_priority = $priority, file_length = $length, last_write_utc = $write, sha256 = $sha,
                parse_status = $status, error = NULL,
                record_count = CASE WHEN $reuse = 1 THEN record_count ELSE 0 END,
                coverage_gap_record_count = CASE WHEN $reuse = 1 THEN coverage_gap_record_count ELSE 0 END,
                string_count = CASE WHEN $reuse = 1 THEN string_count ELSE 0 END,
                content_count = CASE WHEN $reuse = 1 THEN content_count ELSE 0 END,
                encoding_class = CASE WHEN $reuse = 1 THEN encoding_class ELSE NULL END
            WHERE id = $id;
            """;
        SetPluginParameters(command, plugin);
        command.Parameters.AddWithValue("$status", reuse?.ParseStatus ?? "building");
        command.Parameters.AddWithValue("$reuse", reuse is null ? 0 : 1);
        command.Parameters.AddWithValue("$id", pluginId);
        command.ExecuteNonQuery();
    }

    private static long InsertPlugin(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long snapshotId,
        IndexPluginInput plugin)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO plugins(
                snapshot_id, load_order_index, name, physical_path, source_mod, effective_priority,
                file_length, last_write_utc, sha256, parse_status)
            VALUES ($snapshot, $order, $name, $path, $source, $priority, $length, $write, $sha, 'building');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$snapshot", snapshotId);
        SetPluginParameters(command, plugin);
        return (long)command.ExecuteScalar()!;
    }

    private static void SetPluginParameters(SqliteCommand command, IndexPluginInput plugin)
    {
        command.Parameters.AddWithValue("$order", plugin.LoadOrderIndex);
        command.Parameters.AddWithValue("$name", plugin.Name);
        command.Parameters.AddWithValue("$path", plugin.PhysicalPath);
        command.Parameters.AddWithValue("$source", plugin.SourceMod);
        command.Parameters.AddWithValue("$priority", plugin.EffectivePriority);
        command.Parameters.AddWithValue("$length", plugin.FileLength);
        command.Parameters.AddWithValue("$write", plugin.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$sha", plugin.Sha256 is null ? DBNull.Value : plugin.Sha256);
    }

    private IndexPluginCounts IndexPlugin(
        SqliteConnection connection,
        long snapshotId,
        long pluginId,
        FalloutLoc.Core.Configuration.GameMode mode,
        IndexPluginInput plugin,
        CancellationToken cancellationToken)
    {
        using var session = backend.Open(new PluginOpenRequest
        {
            Path = plugin.PhysicalPath,
            Mode = mode,
            LoadOrderIndex = plugin.LoadOrderIndex,
            SourceMod = plugin.SourceMod,
        });
        using var transaction = connection.BeginTransaction();
        using var insertRecord = CreateRecordCommand(connection, transaction);
        using var insertString = CreateStringCommand(connection, transaction);
        using var insertContent = CreateContentCommand(connection, transaction);
        var encodingFields = new List<RecordStringOccurrence>();
        long recordCount = 0;
        long stringCount = 0;
        long contentCount = 0;
        long coverageGapRecords = 0;

        foreach (var record in session.EnumerateMajorRecords(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetRecordParameters(insertRecord, snapshotId, pluginId, record);
            var recordId = (long)insertRecord.ExecuteScalar()!;
            recordCount++;
            if (record.ParseStatus is RecordParseStatus.PartiallyParsed or RecordParseStatus.Unverified)
            {
                coverageGapRecords++;
            }

            foreach (var field in record.Strings)
            {
                SetStringParameters(insertString, recordId, field);
                insertString.ExecuteNonQuery();
                encodingFields.Add(field);
                stringCount++;
            }

            foreach (var content in record.Contents)
            {
                SetContentParameters(insertContent, recordId, content);
                insertContent.ExecuteNonQuery();
                contentCount++;
            }
        }

        var encoding = encodingClassifier.Classify(encodingFields);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE plugins
            SET parse_status = $status, record_count = $records, coverage_gap_record_count = $gaps,
                string_count = $strings, content_count = $contents, encoding_class = $encoding
            WHERE id = $id;
            """;
        update.Parameters.AddWithValue("$status", coverageGapRecords > 0 ? "partiallyParsed" : "parsed");
        update.Parameters.AddWithValue("$records", recordCount);
        update.Parameters.AddWithValue("$gaps", coverageGapRecords);
        update.Parameters.AddWithValue("$strings", stringCount);
        update.Parameters.AddWithValue("$contents", contentCount);
        update.Parameters.AddWithValue("$encoding", encoding.Classification.ToString());
        update.Parameters.AddWithValue("$id", pluginId);
        update.ExecuteNonQuery();
        transaction.Commit();
        return new IndexPluginCounts(recordCount, stringCount, contentCount, coverageGapRecords);
    }

    private static SqliteCommand CreateRecordCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO records(
                snapshot_id, plugin_id, form_key, origin_plugin, record_type, editor_id, is_deleted, is_compressed,
                parse_status, parse_warnings)
            VALUES ($snapshot, $plugin, $form, $origin, $type, $editor, $deleted, $compressed, $status, $warnings);
            SELECT last_insert_rowid();
            """;
        command.Parameters.Add("$snapshot", SqliteType.Integer);
        command.Parameters.Add("$plugin", SqliteType.Integer);
        command.Parameters.Add("$form", SqliteType.Text);
        command.Parameters.Add("$origin", SqliteType.Text);
        command.Parameters.Add("$type", SqliteType.Text);
        command.Parameters.Add("$editor", SqliteType.Text);
        command.Parameters.Add("$deleted", SqliteType.Integer);
        command.Parameters.Add("$compressed", SqliteType.Integer);
        command.Parameters.Add("$status", SqliteType.Text);
        command.Parameters.Add("$warnings", SqliteType.Text);
        command.Prepare();
        return command;
    }

    private static SqliteCommand CreateStringCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO strings(
                record_id, semantic_path, category, text, normalized_text, language,
                encoding_evidence, bytes_sha256, ambiguous)
            VALUES ($record, $semantic, $category, $text, $normalized, $language, $encoding, $sha, $ambiguous);
            """;
        command.Parameters.Add("$record", SqliteType.Integer);
        command.Parameters.Add("$semantic", SqliteType.Text);
        command.Parameters.Add("$category", SqliteType.Text);
        command.Parameters.Add("$text", SqliteType.Text);
        command.Parameters.Add("$normalized", SqliteType.Text);
        command.Parameters.Add("$language", SqliteType.Text);
        command.Parameters.Add("$encoding", SqliteType.Text);
        command.Parameters.Add("$sha", SqliteType.Text);
        command.Parameters.Add("$ambiguous", SqliteType.Integer);
        command.Prepare();
        return command;
    }

    private static SqliteCommand CreateContentCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO record_contents(
                record_id, semantic_path, source_kind, text, normalized_text, encoding_evidence,
                bytes_sha256, ambiguous, is_heuristic)
            VALUES ($record, $semantic, $kind, $text, $normalized, $encoding, $sha, $ambiguous, $heuristic);
            """;
        command.Parameters.Add("$record", SqliteType.Integer);
        command.Parameters.Add("$semantic", SqliteType.Text);
        command.Parameters.Add("$kind", SqliteType.Text);
        command.Parameters.Add("$text", SqliteType.Text);
        command.Parameters.Add("$normalized", SqliteType.Text);
        command.Parameters.Add("$encoding", SqliteType.Text);
        command.Parameters.Add("$sha", SqliteType.Text);
        command.Parameters.Add("$ambiguous", SqliteType.Integer);
        command.Parameters.Add("$heuristic", SqliteType.Integer);
        command.Prepare();
        return command;
    }
    private static void SetRecordParameters(
        SqliteCommand command,
        long snapshotId,
        long pluginId,
        RecordOccurrence record)
    {
        command.Parameters["$snapshot"].Value = snapshotId;
        command.Parameters["$plugin"].Value = pluginId;
        command.Parameters["$form"].Value = record.FormKey;
        command.Parameters["$origin"].Value = record.OriginPlugin;
        command.Parameters["$type"].Value = record.RecordType;
        command.Parameters["$editor"].Value = record.EditorId is null ? DBNull.Value : record.EditorId;
        command.Parameters["$deleted"].Value = record.IsDeleted ? 1 : 0;
        command.Parameters["$compressed"].Value = record.IsCompressed ? 1 : 0;
        command.Parameters["$status"].Value = RecordParseStatusValue(record.ParseStatus);
        command.Parameters["$warnings"].Value = record.ParseWarnings.Count == 0
            ? DBNull.Value
            : JsonSerializer.Serialize(record.ParseWarnings
                .Take(20)
                .Select(warning => warning[..Math.Min(warning.Length, 1000)]));
    }

    private static void SetStringParameters(SqliteCommand command, long recordId, RecordStringOccurrence field)
    {
        command.Parameters["$record"].Value = recordId;
        command.Parameters["$semantic"].Value = field.SemanticPath;
        command.Parameters["$category"].Value = field.Category;
        command.Parameters["$text"].Value = field.Text is null ? DBNull.Value : field.Text;
        command.Parameters["$normalized"].Value = TextNormalizer.Normalize(field.Text) is { } normalized
            ? normalized
            : DBNull.Value;
        command.Parameters["$language"].Value = field.Language.ToString();
        command.Parameters["$encoding"].Value = field.EncodingEvidence.ToString();
        command.Parameters["$sha"].Value = field.RecoveredBytesSha256 is null ? DBNull.Value : field.RecoveredBytesSha256;
        command.Parameters["$ambiguous"].Value = field.Ambiguous ? 1 : 0;
    }

    private static void SetContentParameters(SqliteCommand command, long recordId, RecordContentOccurrence content)
    {
        command.Parameters["$record"].Value = recordId;
        command.Parameters["$semantic"].Value = content.SemanticPath;
        command.Parameters["$kind"].Value = content.SourceKind.ToString();
        command.Parameters["$text"].Value = content.Text is null ? DBNull.Value : content.Text;
        command.Parameters["$normalized"].Value = TextNormalizer.Normalize(content.Text) is { } normalized
            ? normalized
            : DBNull.Value;
        command.Parameters["$encoding"].Value = content.EncodingEvidence.ToString();
        command.Parameters["$sha"].Value = content.RecoveredBytesSha256 is null
            ? DBNull.Value
            : content.RecoveredBytesSha256;
        command.Parameters["$ambiguous"].Value = content.Ambiguous ? 1 : 0;
        command.Parameters["$heuristic"].Value = content.IsHeuristic ? 1 : 0;
    }
    private static string RecordParseStatusValue(RecordParseStatus status) => status switch
    {
        RecordParseStatus.Parsed => "parsed",
        RecordParseStatus.PartiallyParsed => "partiallyParsed",
        RecordParseStatus.NotApplicable => "notApplicable",
        RecordParseStatus.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported record parse status."),
    };

    private static void MarkPluginFailed(SqliteConnection connection, long pluginId, Exception exception)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE plugins SET parse_status = 'failed', error = $error WHERE id = $id;";
        var detail = $"{exception.GetType().Name}: {exception.Message}";
        command.Parameters.AddWithValue("$error", detail[..Math.Min(detail.Length, 4000)]);
        command.Parameters.AddWithValue("$id", pluginId);
        command.ExecuteNonQuery();
    }

    private static void MarkSnapshotComplete(SqliteConnection connection, long snapshotId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE snapshots SET status = 'complete' WHERE id = $id;";
        command.Parameters.AddWithValue("$id", snapshotId);
        command.ExecuteNonQuery();
    }

    private sealed class PluginWorkItem(IndexPluginInput plugin, long pluginId)
    {
        public IndexPluginInput Plugin { get; } = plugin;
        public long PluginId { get; } = pluginId;
        public ReusablePlugin? Reuse { get; set; }
        public bool Completed { get; set; }
    }

    private sealed record ExistingPlugin(
        long PluginId,
        string Name,
        string PhysicalPath,
        long FileLength,
        string LastWriteUtc,
        string? Sha256,
        string ParseStatus,
        long RecordCount,
        long CoverageGapRecordCount,
        long StringCount,
        long ContentCount,
        string? EncodingClass)
    {
        public bool IsReusable(IndexPluginInput plugin) =>
            ParseStatus is "parsed" or "partiallyParsed"
            && string.Equals(PhysicalPath, plugin.PhysicalPath, StringComparison.OrdinalIgnoreCase)
            && FileLength == plugin.FileLength
            && LastWriteUtc == plugin.LastWriteUtc.ToString("O", CultureInfo.InvariantCulture)
            && (plugin.Sha256 is null || string.Equals(Sha256, plugin.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ReusablePlugin(
        long PluginId,
        string ParseStatus,
        long RecordCount,
        long CoverageGapRecordCount,
        long StringCount,
        long ContentCount,
        string? EncodingClass);

    private sealed record IndexPluginCounts(long Records, long Strings, long Contents, long CoverageGapRecords);
}
