using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FalloutLoc.Backends.Models;
using FalloutLoc.Index.Models;
using Microsoft.Data.Sqlite;

namespace FalloutLoc.Index;

public sealed class SqliteIndexRepository(string databasePath) : IIndexQuery
{
    private readonly Dictionary<string, IReadOnlyList<IndexedPhysicalProvider>> _providerCache =
        new(StringComparer.OrdinalIgnoreCase);
    private IndexSnapshotStatus? _statusCache;

    public IReadOnlyList<IndexedStringMatch> Find(string query, int limit = 50) =>
        SearchText(new IndexedTextSearchRequest
        {
            Query = query,
            IgnoreCase = true,
            Limit = limit,
        }).Items;

    public IndexedPage<IndexedStringMatch> SearchText(IndexedTextSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ValidatePageLimit(request.Limit);
        if (request.Mode == IndexedTextSearchMode.Regex && request.Query.Length > 1000)
        {
            throw new ArgumentException("A regular expression cannot exceed 1000 characters.", nameof(request));
        }

        var scope = string.Join('\0', "text-v1", request.Query, request.Mode, request.IgnoreCase,
            request.PluginName, request.RecordType, request.Category, request.WinnerOnly);
        var offset = DecodeCursor(request.Cursor, scope);
        using var connection = OpenValidated();
        Regex? regex = null;
        if (request.Mode == IndexedTextSearchMode.Regex)
        {
            try
            {
                regex = new Regex(
                    request.Query,
                    RegexOptions.CultureInvariant | (request.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
                    TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"Invalid regular expression: {exception.Message}", nameof(request), exception);
            }

            connection.CreateFunction<string?, long>(
                "faloudit_regex",
                value => value is not null && regex.IsMatch(value) ? 1L : 0L,
                isDeterministic: true);
        }

        const string normalWinnerExpression = """
            NOT EXISTS (
                SELECT 1
                FROM records later_record
                JOIN plugins later_plugin ON later_plugin.id = later_record.plugin_id
                WHERE later_record.snapshot_id = r.snapshot_id
                  AND later_record.form_key = r.form_key COLLATE NOCASE
                  AND later_plugin.load_order_index > p.load_order_index
            )
            """;
        var normalized = TextNormalizer.Normalize(request.Query)!;
        var escaped = EscapeLike(normalized);
        var matchPredicate = request.Mode switch
        {
            IndexedTextSearchMode.Exact when request.IgnoreCase => "u.normalized_text = $normalized",
            IndexedTextSearchMode.Exact => "u.text = $query COLLATE BINARY",
            IndexedTextSearchMode.Contains when request.IgnoreCase => "u.normalized_text LIKE $pattern ESCAPE '\\'",
            IndexedTextSearchMode.Contains => "instr(u.text, $query) > 0",
            IndexedTextSearchMode.Regex => "faloudit_regex(u.text) = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unsupported text search mode."),
        };
        var rankExpression = request.Mode switch
        {
            IndexedTextSearchMode.Exact => "(0 + 0)",
            IndexedTextSearchMode.Regex => "(3 + 0)",
            _ when request.IgnoreCase => """
                CASE
                    WHEN u.normalized_text = $normalized THEN 0
                    WHEN u.normalized_text LIKE $prefix ESCAPE '\' THEN 1
                    ELSE 2
                END
                """,
            _ => """
                CASE
                    WHEN u.text = $query COLLATE BINARY THEN 0
                    WHEN instr(u.text, $query) = 1 THEN 1
                    ELSE 2
                END
                """,
        };
        var predicates = new List<string> { matchPredicate };
        if (!string.IsNullOrWhiteSpace(request.PluginName))
        {
            predicates.Add("u.plugin_name = $plugin COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            predicates.Add("u.record_type = $type COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            predicates.Add("u.category = $category COLLATE NOCASE");
        }

        if (request.WinnerOnly)
        {
            predicates.Add("u.is_winner = 1");
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH game_setting_sources AS (
                SELECT 'gmst:' || e.editor_id AS form_key,
                       'GameSettingString' AS record_type,
                       e.editor_id,
                       CASE WHEN snapshot.mode = 'Fallout3' THEN 'Fallout3.exe' ELSE 'FalloutNV.exe' END AS plugin_name,
                       -1 AS load_order_index,
                       COALESCE(snapshot.runtime_executable_path, snapshot.engine_game_setting_catalog_path, '') AS physical_path,
                       'Game Runtime' AS source_mod,
                       'Data' AS semantic_path,
                       'game-setting' AS category,
                       e.default_text AS text,
                       e.normalized_text,
                       e.language,
                       e.encoding_evidence,
                       e.ambiguous,
                       'engineDefault' AS source_kind,
                       0 AS precedence_group,
                       0 AS precedence_order,
                       e.id AS stable_id
                FROM engine_game_settings e
                JOIN snapshots snapshot ON snapshot.id = e.snapshot_id

                UNION ALL

                SELECT 'gmst:' || r.editor_id,
                       'GameSettingString', r.editor_id,
                       p.name, p.load_order_index, p.physical_path, p.source_mod,
                       s.semantic_path, s.category, s.text, s.normalized_text,
                       s.language, s.encoding_evidence, s.ambiguous,
                       'plugin', 1, p.load_order_index, r.id
                FROM strings s
                JOIN records r ON r.id = s.record_id
                JOIN plugins p ON p.id = r.plugin_id
                WHERE r.record_type = 'GameSettingString' COLLATE NOCASE
                  AND r.editor_id IS NOT NULL
                  AND s.semantic_path = 'Data' COLLATE NOCASE

                UNION ALL

                SELECT 'gmst:' || post.editor_id,
                       'GameSettingString', post.editor_id,
                       post.logical_path, 2147483647, post.physical_path, post.source_mod,
                       'Data', 'game-setting', post.text, post.normalized_text,
                       post.language, post.encoding_evidence, post.ambiguous,
                       'postPluginIni', 2, post.sequence, post.id
                FROM post_plugin_game_settings post
            ),
            ranked_game_settings AS (
                SELECT source.*,
                       CASE WHEN ROW_NUMBER() OVER (
                           PARTITION BY source.editor_id COLLATE NOCASE
                           ORDER BY source.precedence_group DESC,
                                    source.precedence_order DESC,
                                    source.stable_id DESC
                       ) = 1 THEN 1 ELSE 0 END AS is_winner
                FROM game_setting_sources source
            ),
            unified_strings AS (
                SELECT r.form_key, r.record_type, r.editor_id,
                       p.name AS plugin_name, p.load_order_index, p.physical_path, p.source_mod,
                       s.semantic_path, s.category, s.text, s.normalized_text,
                       s.language, s.encoding_evidence, s.ambiguous,
                       CASE WHEN {normalWinnerExpression} THEN 1 ELSE 0 END AS is_winner,
                       'plugin' AS source_kind,
                       p.load_order_index AS source_order,
                       s.id AS stable_id
                FROM strings s
                JOIN records r ON r.id = s.record_id
                JOIN plugins p ON p.id = r.plugin_id
                WHERE r.record_type <> 'GameSettingString' COLLATE NOCASE
                   OR r.editor_id IS NULL

                UNION ALL

                SELECT form_key, record_type, editor_id, plugin_name, load_order_index,
                       physical_path, source_mod, semantic_path, category, text, normalized_text,
                       language, encoding_evidence, ambiguous, is_winner, source_kind,
                       (precedence_group * 1000000) + precedence_order AS source_order,
                       stable_id
                FROM ranked_game_settings
            )
            SELECT u.form_key, u.record_type, u.editor_id,
                   u.plugin_name, u.load_order_index, u.physical_path, u.source_mod,
                   u.semantic_path, u.category, u.text, u.language, u.encoding_evidence, u.ambiguous,
                   u.is_winner, u.source_kind
            FROM unified_strings u
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY
                {rankExpression},
                u.is_winner DESC,
                u.source_order DESC,
                u.form_key COLLATE NOCASE,
                u.semantic_path COLLATE NOCASE,
                u.stable_id
            LIMIT $take OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$query", request.Query);
        command.Parameters.AddWithValue("$normalized", normalized);
        command.Parameters.AddWithValue("$pattern", $"%{escaped}%");
        command.Parameters.AddWithValue("$prefix", $"{escaped}%");
        command.Parameters.AddWithValue("$plugin", request.PluginName is null ? DBNull.Value : request.PluginName);
        command.Parameters.AddWithValue("$type", request.RecordType is null ? DBNull.Value : request.RecordType);
        command.Parameters.AddWithValue("$category", request.Category is null ? DBNull.Value : request.Category);
        command.Parameters.AddWithValue("$take", request.Limit + 1);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var results = new List<IndexedStringMatch>();
        while (reader.Read())
        {
            results.Add(new IndexedStringMatch
            {
                FormKey = reader.GetString(0),
                RecordType = reader.GetString(1),
                EditorId = reader.IsDBNull(2) ? null : reader.GetString(2),
                PluginName = reader.GetString(3),
                LoadOrderIndex = reader.GetInt32(4),
                PhysicalPath = reader.GetString(5),
                SourceMod = reader.GetString(6),
                SemanticPath = reader.GetString(7),
                Category = reader.GetString(8),
                Text = reader.IsDBNull(9) ? null : reader.GetString(9),
                Language = Enum.Parse<TextLanguageKind>(reader.GetString(10)),
                EncodingEvidence = Enum.Parse<StringEncodingEvidence>(reader.GetString(11)),
                Ambiguous = reader.GetBoolean(12),
                IsWinningOverride = reader.GetBoolean(13),
                SourceKind = reader.GetString(14),
            });
        }

        return CreatePage(results, request.Limit, offset, scope);
    }

    public IndexedPage<IndexedContentMatch> SearchContent(IndexedContentSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        ValidatePageLimit(request.Limit);
        if (request.Mode == IndexedTextSearchMode.Regex && request.Query.Length > 1000)
        {
            throw new ArgumentException("A regular expression cannot exceed 1000 characters.", nameof(request));
        }

        var scope = string.Join('\0', "content-v1", request.Query, request.Mode, request.IgnoreCase,
            request.PluginName, request.RecordType, request.SourceKind, request.WinnerOnly);
        var offset = DecodeCursor(request.Cursor, scope);
        using var connection = OpenValidated();
        Regex? regex = null;
        if (request.Mode == IndexedTextSearchMode.Regex)
        {
            try
            {
                regex = new Regex(
                    request.Query,
                    RegexOptions.CultureInvariant | (request.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None),
                    TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException($"Invalid regular expression: {exception.Message}", nameof(request), exception);
            }

            connection.CreateFunction<string?, long>(
                "faloudit_content_regex",
                value => value is not null && regex.IsMatch(value) ? 1L : 0L,
                isDeterministic: true);
        }

        const string winnerExpression = """
            NOT EXISTS (
                SELECT 1
                FROM records later_record
                JOIN plugins later_plugin ON later_plugin.id = later_record.plugin_id
                WHERE later_record.snapshot_id = r.snapshot_id
                  AND later_record.form_key = r.form_key COLLATE NOCASE
                  AND later_plugin.load_order_index > p.load_order_index
            )
            """;
        var normalized = TextNormalizer.Normalize(request.Query)!;
        var escaped = EscapeLike(normalized);
        var matchPredicate = request.Mode switch
        {
            IndexedTextSearchMode.Exact when request.IgnoreCase => "c.normalized_text = $normalized",
            IndexedTextSearchMode.Exact => "c.text = $query COLLATE BINARY",
            IndexedTextSearchMode.Contains when request.IgnoreCase => "instr(c.normalized_text, $normalized) > 0",
            IndexedTextSearchMode.Contains => "instr(c.text, $query) > 0",
            IndexedTextSearchMode.Regex => "faloudit_content_regex(c.text) = 1",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unsupported content search mode."),
        };
        var rankExpression = request.Mode switch
        {
            IndexedTextSearchMode.Exact => "(0 + 0)",
            IndexedTextSearchMode.Regex => "(3 + 0)",
            _ when request.IgnoreCase => """
                CASE
                    WHEN c.normalized_text = $normalized THEN 0
                    WHEN instr(c.normalized_text, $normalized) = 1 THEN 1
                    ELSE 2
                END
                """,
            _ => """
                CASE
                    WHEN c.text = $query COLLATE BINARY THEN 0
                    WHEN instr(c.text, $query) = 1 THEN 1
                    ELSE 2
                END
                """,
        };
        var predicates = new List<string> { matchPredicate };
        if (!string.IsNullOrWhiteSpace(request.PluginName))
        {
            predicates.Add("p.name = $plugin COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            predicates.Add("r.record_type = $type COLLATE NOCASE");
        }

        if (request.SourceKind is not null)
        {
            predicates.Add("c.source_kind = $kind");
        }

        if (request.WinnerOnly)
        {
            predicates.Add(winnerExpression);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT r.form_key, r.record_type, r.editor_id,
                   p.name, p.load_order_index, p.physical_path, p.source_mod,
                   c.semantic_path, c.source_kind, c.text, c.encoding_evidence, c.ambiguous, c.is_heuristic,
                   CASE WHEN {winnerExpression} THEN 1 ELSE 0 END AS is_winner
            FROM record_contents c
            JOIN records r ON r.id = c.record_id
            JOIN plugins p ON p.id = r.plugin_id
            WHERE {string.Join(" AND ", predicates)}
            ORDER BY
                {rankExpression},
                is_winner DESC,
                p.load_order_index DESC,
                r.form_key COLLATE NOCASE,
                c.semantic_path COLLATE NOCASE,
                c.id
            LIMIT $take OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$query", request.Query);
        command.Parameters.AddWithValue("$normalized", normalized);
        command.Parameters.AddWithValue("$pattern", $"%{escaped}%");
        command.Parameters.AddWithValue("$prefix", $"{escaped}%");
        command.Parameters.AddWithValue("$plugin", request.PluginName is null ? DBNull.Value : request.PluginName);
        command.Parameters.AddWithValue("$type", request.RecordType is null ? DBNull.Value : request.RecordType);
        command.Parameters.AddWithValue("$kind", request.SourceKind is null ? DBNull.Value : request.SourceKind.ToString());
        command.Parameters.AddWithValue("$take", request.Limit + 1);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var results = new List<IndexedContentMatch>();
        while (reader.Read())
        {
            var content = reader.IsDBNull(9) ? string.Empty : reader.GetString(9);
            var context = BuildContentContext(content, request, regex);
            results.Add(new IndexedContentMatch
            {
                FormKey = reader.GetString(0),
                RecordType = reader.GetString(1),
                EditorId = reader.IsDBNull(2) ? null : reader.GetString(2),
                PluginName = reader.GetString(3),
                LoadOrderIndex = reader.GetInt32(4),
                PhysicalPath = reader.GetString(5),
                SourceMod = reader.GetString(6),
                SemanticPath = reader.GetString(7),
                SourceKind = Enum.Parse<RecordContentSourceKind>(reader.GetString(8)),
                Context = context.Text,
                ContextStart = context.Start,
                ContentLength = content.Length,
                EncodingEvidence = Enum.Parse<StringEncodingEvidence>(reader.GetString(10)),
                Ambiguous = reader.GetBoolean(11),
                IsHeuristic = reader.GetBoolean(12),
                IsWinningOverride = reader.GetBoolean(13),
            });
        }

        return CreatePage(results, request.Limit, offset, scope);
    }
    public IndexedPage<IndexedRecordMatch> FindByEditorId(IndexedEditorIdSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EditorId);
        ValidatePageLimit(request.Limit);
        if (string.IsNullOrWhiteSpace(request.PluginName)
            && (string.IsNullOrWhiteSpace(request.RecordType)
                || request.RecordType.Equals("GameSettingString", StringComparison.OrdinalIgnoreCase)))
        {
            var gameSetting = TraceGameSetting(request.EditorId);
            if (gameSetting.Chain.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(request.Cursor))
                {
                    throw new ArgumentException("A GameSetting EditorID result has no additional page.", nameof(request));
                }

                var winner = gameSetting.Chain[^1];
                return new IndexedPage<IndexedRecordMatch>
                {
                    Items =
                    [
                        new IndexedRecordMatch
                        {
                            FormKey = gameSetting.FormKey,
                            OriginPlugin = gameSetting.Chain[0].PluginName,
                            RecordType = "GameSettingString",
                            EditorId = winner.EditorId,
                            WinningPluginName = winner.PluginName,
                            WinningLoadOrderIndex = winner.LoadOrderIndex,
                            WinningPhysicalPath = winner.PhysicalPath,
                            WinningSourceMod = winner.SourceMod,
                            WinningEffectivePriority = winner.EffectivePriority,
                            IsDeleted = winner.IsDeleted,
                            OverrideCount = gameSetting.Chain.Count,
                        },
                    ],
                    Limit = request.Limit,
                    HasMore = false,
                };
            }
        }

        var scope = string.Join('\0', "edid-v1", request.EditorId.ToUpperInvariant(),
            request.PluginName, request.RecordType, request.WinnerOnly);
        var offset = DecodeCursor(request.Cursor, scope);
        using var connection = OpenValidated();
        const string matchedWinner = """
            NOT EXISTS (
                SELECT 1
                FROM records later_record
                JOIN plugins later_plugin ON later_plugin.id = later_record.plugin_id
                WHERE later_record.snapshot_id = candidate.snapshot_id
                  AND later_record.form_key = candidate.form_key COLLATE NOCASE
                  AND later_plugin.load_order_index > matched_plugin.load_order_index
            )
            """;
        var predicates = new List<string> { "candidate.editor_id = $editor COLLATE NOCASE" };
        if (!string.IsNullOrWhiteSpace(request.PluginName))
        {
            predicates.Add("matched_plugin.name = $plugin COLLATE NOCASE");
        }

        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            predicates.Add("candidate.record_type = $type COLLATE NOCASE");
        }

        if (request.WinnerOnly)
        {
            predicates.Add(matchedWinner);
        }

        return ReadRecordPage(
            connection,
            string.Join(" AND ", predicates),
            command =>
            {
                command.Parameters.AddWithValue("$editor", request.EditorId);
                command.Parameters.AddWithValue("$plugin", request.PluginName is null ? DBNull.Value : request.PluginName);
                command.Parameters.AddWithValue("$type", request.RecordType is null ? DBNull.Value : request.RecordType);
            },
            request.Limit,
            offset,
            scope);
    }

    public IndexedFormLookupResult ResolveForm(string input, int limit = 50, string? cursor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ValidatePageLimit(limit);
        var parsed = ParseFormLookup(input);
        using var connection = OpenValidated();
        string? resolvedPlugin = parsed.PluginName;
        int? runtimeIndex = parsed.RuntimeLoadOrderIndex;
        IndexedPage<IndexedRecordMatch> matches;
        string scope;

        if (parsed.Kind == IndexedFormLookupKind.RuntimeFormId)
        {
            using var pluginCommand = connection.CreateCommand();
            pluginCommand.CommandText = "SELECT name FROM plugins WHERE load_order_index = $index LIMIT 1;";
            pluginCommand.Parameters.AddWithValue("$index", parsed.RuntimeLoadOrderIndex!.Value);
            resolvedPlugin = pluginCommand.ExecuteScalar() as string;
            scope = string.Join('\0', "form-v1", parsed.Kind, parsed.LocalFormId, parsed.RuntimeLoadOrderIndex);
            var offset = DecodeCursor(cursor, scope);
            matches = resolvedPlugin is null
                ? CreatePage(new List<IndexedRecordMatch>(), limit, offset, scope)
                : ReadRecordPage(
                    connection,
                    "candidate.form_key = $form COLLATE NOCASE",
                    command => command.Parameters.AddWithValue("$form", $"{parsed.LocalFormId}:{resolvedPlugin}"),
                    limit,
                    offset,
                    scope);
        }
        else if (parsed.Kind == IndexedFormLookupKind.FormKey)
        {
            scope = string.Join('\0', "form-v1", parsed.Kind, parsed.LocalFormId, parsed.PluginName!.ToUpperInvariant());
            var offset = DecodeCursor(cursor, scope);
            matches = ReadRecordPage(
                connection,
                "candidate.form_key = $form COLLATE NOCASE",
                command => command.Parameters.AddWithValue("$form", $"{parsed.LocalFormId}:{parsed.PluginName}"),
                limit,
                offset,
                scope);
            resolvedPlugin = matches.Items.FirstOrDefault()?.OriginPlugin ?? parsed.PluginName;
        }
        else
        {
            scope = string.Join('\0', "form-v1", parsed.Kind, parsed.LocalFormId);
            var offset = DecodeCursor(cursor, scope);
            matches = ReadRecordPage(
                connection,
                "candidate.form_key LIKE $prefix ESCAPE '\\'",
                command => command.Parameters.AddWithValue("$prefix", $"{parsed.LocalFormId}:%"),
                limit,
                offset,
                scope);
        }

        return new IndexedFormLookupResult
        {
            Input = input,
            Kind = parsed.Kind,
            LocalFormId = parsed.LocalFormId,
            RuntimeLoadOrderIndex = runtimeIndex,
            ResolvedOriginPlugin = resolvedPlugin,
            IsAmbiguous = parsed.Kind == IndexedFormLookupKind.LocalFormId
                && (matches.HasMore || matches.Items.Count > 1),
            Matches = matches,
        };
    }

    public IndexedOverrideTrace Trace(string formKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formKey);
        if (formKey.StartsWith("gmst:", StringComparison.OrdinalIgnoreCase))
        {
            var editorId = formKey[5..].Trim();
            if (editorId.Length == 0)
            {
                throw new ArgumentException("A synthetic GameSetting key must include an EditorID after 'gmst:'.", nameof(formKey));
            }

            return TraceGameSetting(editorId);
        }

        using var connection = OpenValidated();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, p.name, p.load_order_index, p.physical_path, p.source_mod, p.effective_priority,
                   r.record_type, r.editor_id, r.is_deleted, r.is_compressed
            FROM records r
            JOIN plugins p ON p.id = r.plugin_id
            WHERE r.form_key = $form COLLATE NOCASE
            ORDER BY p.load_order_index;
            """;
        command.Parameters.AddWithValue("$form", formKey);
        var raw = new List<RawOverride>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                raw.Add(new RawOverride(
                    reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                    reader.GetString(4), reader.GetInt64(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetBoolean(8), reader.GetBoolean(9)));
            }
        }

        var chain = new List<IndexedOverride>(raw.Count);
        foreach (var item in raw)
        {
            chain.Add(new IndexedOverride
            {
                PluginName = item.PluginName,
                LoadOrderIndex = item.LoadOrderIndex,
                PhysicalPath = item.PhysicalPath,
                SourceMod = item.SourceMod,
                EffectivePriority = item.EffectivePriority,
                RecordType = item.RecordType,
                EditorId = item.EditorId,
                IsDeleted = item.IsDeleted,
                IsCompressed = item.IsCompressed,
                IsWinner = item == raw[^1],
                Strings = ReadStrings(connection, item.RecordId),
            });
        }

        return new IndexedOverrideTrace { FormKey = formKey, Chain = chain };
    }

    private IndexedOverrideTrace TraceGameSetting(string editorId)
    {
        using var connection = OpenValidated();
        using (var canonical = connection.CreateCommand())
        {
            canonical.CommandText = """
                SELECT editor_id FROM engine_game_settings WHERE editor_id = $editor COLLATE NOCASE
                UNION ALL
                SELECT editor_id FROM records
                 WHERE record_type = 'GameSettingString' COLLATE NOCASE
                   AND editor_id = $editor COLLATE NOCASE
                UNION ALL
                SELECT editor_id FROM post_plugin_game_settings WHERE editor_id = $editor COLLATE NOCASE
                LIMIT 1;
                """;
            canonical.Parameters.AddWithValue("$editor", editorId);
            editorId = canonical.ExecuteScalar() as string ?? editorId;
        }

        var chain = new List<IndexedOverride>();

        using (var engine = connection.CreateCommand())
        {
            engine.CommandText = """
                SELECT CASE WHEN snapshot.mode = 'Fallout3' THEN 'Fallout3.exe' ELSE 'FalloutNV.exe' END,
                       COALESCE(snapshot.runtime_executable_path, snapshot.engine_game_setting_catalog_path, ''),
                       e.default_text, e.language, e.encoding_evidence, e.ambiguous
                FROM engine_game_settings e
                JOIN snapshots snapshot ON snapshot.id = e.snapshot_id
                WHERE e.editor_id = $editor COLLATE NOCASE
                ORDER BY e.id DESC LIMIT 1;
                """;
            engine.Parameters.AddWithValue("$editor", editorId);
            using var reader = engine.ExecuteReader();
            if (reader.Read())
            {
                chain.Add(new IndexedOverride
                {
                    PluginName = reader.GetString(0),
                    LoadOrderIndex = -1,
                    PhysicalPath = reader.GetString(1),
                    SourceMod = "Game Runtime",
                    EffectivePriority = -1,
                    RecordType = "GameSettingString",
                    EditorId = editorId,
                    IsDeleted = false,
                    IsCompressed = false,
                    IsWinner = false,
                    Strings =
                    [
                        new IndexedTraceString
                        {
                            SemanticPath = "Data",
                            Category = "game-setting",
                            Text = reader.GetString(2),
                            Language = Enum.Parse<TextLanguageKind>(reader.GetString(3)),
                            EncodingEvidence = Enum.Parse<StringEncodingEvidence>(reader.GetString(4)),
                            Ambiguous = reader.GetBoolean(5),
                        },
                    ],
                });
            }
        }

        var pluginRows = new List<RawOverride>();
        using (var plugins = connection.CreateCommand())
        {
            plugins.CommandText = """
                SELECT r.id, p.name, p.load_order_index, p.physical_path, p.source_mod, p.effective_priority,
                       r.record_type, r.editor_id, r.is_deleted, r.is_compressed
                FROM records r
                JOIN plugins p ON p.id = r.plugin_id
                WHERE r.record_type = 'GameSettingString' COLLATE NOCASE
                  AND r.editor_id = $editor COLLATE NOCASE
                ORDER BY p.load_order_index, r.id;
                """;
            plugins.Parameters.AddWithValue("$editor", editorId);
            using var reader = plugins.ExecuteReader();
            while (reader.Read())
            {
                pluginRows.Add(new RawOverride(
                    reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
                    reader.GetString(4), reader.GetInt64(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetBoolean(8), reader.GetBoolean(9)));
            }
        }

        foreach (var item in pluginRows)
        {
            chain.Add(new IndexedOverride
            {
                PluginName = item.PluginName,
                LoadOrderIndex = item.LoadOrderIndex,
                PhysicalPath = item.PhysicalPath,
                SourceMod = item.SourceMod,
                EffectivePriority = item.EffectivePriority,
                RecordType = "GameSettingString",
                EditorId = editorId,
                IsDeleted = item.IsDeleted,
                IsCompressed = item.IsCompressed,
                IsWinner = false,
                Strings = ReadStrings(connection, item.RecordId)
                    .Where(field => field.SemanticPath.Equals("Data", StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
            });
        }

        using (var postPlugin = connection.CreateCommand())
        {
            postPlugin.CommandText = """
                SELECT logical_path, physical_path, source_mod, effective_priority,
                       text, language, encoding_evidence, ambiguous
                FROM post_plugin_game_settings
                WHERE editor_id = $editor COLLATE NOCASE
                ORDER BY sequence, id;
                """;
            postPlugin.Parameters.AddWithValue("$editor", editorId);
            using var reader = postPlugin.ExecuteReader();
            while (reader.Read())
            {
                chain.Add(new IndexedOverride
                {
                    PluginName = reader.GetString(0),
                    LoadOrderIndex = int.MaxValue,
                    PhysicalPath = reader.GetString(1),
                    SourceMod = reader.GetString(2),
                    EffectivePriority = reader.GetInt64(3),
                    RecordType = "GameSettingString",
                    EditorId = editorId,
                    IsDeleted = false,
                    IsCompressed = false,
                    IsWinner = false,
                    Strings =
                    [
                        new IndexedTraceString
                        {
                            SemanticPath = "Data",
                            Category = "game-setting",
                            Text = reader.GetString(4),
                            Language = Enum.Parse<TextLanguageKind>(reader.GetString(5)),
                            EncodingEvidence = Enum.Parse<StringEncodingEvidence>(reader.GetString(6)),
                            Ambiguous = reader.GetBoolean(7),
                        },
                    ],
                });
            }
        }

        if (chain.Count > 0)
        {
            chain[^1] = chain[^1] with { IsWinner = true };
        }

        return new IndexedOverrideTrace
        {
            FormKey = $"gmst:{editorId}",
            Chain = chain,
        };
    }

    public IReadOnlyList<string> FindRegressionCandidateFormKeys(string? winningPlugin, int limit)
        => FindDiagnosticCandidateFormKeys(new IndexedDiagnosticCandidateRequest
        {
            Kind = IndexedDiagnosticKind.Regressions,
            WinningPlugin = winningPlugin,
            Limit = limit,
        }).Items;

    public IReadOnlyList<string> FindUntranslatedCandidateFormKeys(string? winningPlugin, int limit)
        => FindDiagnosticCandidateFormKeys(new IndexedDiagnosticCandidateRequest
        {
            Kind = IndexedDiagnosticKind.Untranslated,
            WinningPlugin = winningPlugin,
            Limit = limit,
        }).Items;

    public IndexedPage<string> FindDiagnosticCandidateFormKeys(IndexedDiagnosticCandidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePageLimit(request.Limit);
        var scope = string.Join('\0', "diagnostic-v1", request.Kind, request.WinningPlugin,
            request.SourceMod, request.RecordType, request.Category);
        var offset = DecodeCursor(request.Cursor, scope);

        using var connection = OpenValidated();
        using var command = connection.CreateCommand();
        command.CommandText = request.Kind == IndexedDiagnosticKind.Regressions
            ? """
              SELECT DISTINCT winner.form_key
              FROM strings earlier_string
              JOIN records earlier_record ON earlier_record.id = earlier_string.record_id
              JOIN plugins earlier_plugin ON earlier_plugin.id = earlier_record.plugin_id
              JOIN records winner
                ON winner.snapshot_id = earlier_record.snapshot_id
               AND winner.form_key = earlier_record.form_key COLLATE NOCASE
              JOIN plugins winning_plugin ON winning_plugin.id = winner.plugin_id
              JOIN strings winning_string
                ON winning_string.record_id = winner.id
               AND winning_string.semantic_path = earlier_string.semantic_path
              WHERE (
                    earlier_string.language = 'Target'
                    OR (
                        winning_string.language = 'Source'
                        AND earlier_string.text IS NOT NULL
                        AND TRIM(earlier_string.text) <> ''
                        AND earlier_string.text <> winning_string.text
                        AND EXISTS (
                            SELECT 1
                            FROM records source_record
                            JOIN plugins source_plugin ON source_plugin.id = source_record.plugin_id
                            JOIN strings source_string
                              ON source_string.record_id = source_record.id
                             AND source_string.semantic_path = winning_string.semantic_path
                            WHERE source_record.snapshot_id = winner.snapshot_id
                              AND source_record.form_key = winner.form_key COLLATE NOCASE
                              AND source_plugin.load_order_index < earlier_plugin.load_order_index
                              AND source_string.language = 'Source'
                              AND source_string.text = winning_string.text
                        )
                    )
                )
                AND winning_string.language IN ('Source', 'Empty', 'Other')
                AND earlier_plugin.load_order_index < winning_plugin.load_order_index
                AND winning_plugin.parse_status IN ('parsed', 'partiallyParsed')
                AND ($plugin IS NULL OR winning_plugin.name = $plugin COLLATE NOCASE)
                AND ($mod IS NULL OR winning_plugin.source_mod = $mod COLLATE NOCASE)
                AND ($type IS NULL OR winner.record_type = $type COLLATE NOCASE)
                AND ($category IS NULL OR winning_string.category = $category COLLATE NOCASE)
                AND NOT EXISTS (
                    SELECT 1
                    FROM records later_record
                    JOIN plugins later_plugin ON later_plugin.id = later_record.plugin_id
                    WHERE later_record.snapshot_id = winner.snapshot_id
                      AND later_record.form_key = winner.form_key COLLATE NOCASE
                      AND later_plugin.load_order_index > winning_plugin.load_order_index
                )
              ORDER BY winner.form_key COLLATE NOCASE
              LIMIT $take OFFSET $offset;
              """
            : """
              SELECT DISTINCT winner.form_key
              FROM records winner
              JOIN plugins winning_plugin ON winning_plugin.id = winner.plugin_id
              JOIN strings winning_string ON winning_string.record_id = winner.id
              WHERE winning_plugin.parse_status IN ('parsed', 'partiallyParsed')
                AND winning_string.language = 'Source'
                AND ($plugin IS NULL OR winning_plugin.name = $plugin COLLATE NOCASE)
                AND ($mod IS NULL OR winning_plugin.source_mod = $mod COLLATE NOCASE)
                AND ($type IS NULL OR winner.record_type = $type COLLATE NOCASE)
                AND ($category IS NULL OR winning_string.category = $category COLLATE NOCASE)
                AND NOT EXISTS (
                    SELECT 1
                    FROM records later_record
                    JOIN plugins later_plugin ON later_plugin.id = later_record.plugin_id
                    WHERE later_record.snapshot_id = winner.snapshot_id
                      AND later_record.form_key = winner.form_key COLLATE NOCASE
                      AND later_plugin.load_order_index > winning_plugin.load_order_index
                )
                AND NOT EXISTS (
                  SELECT 1
                  FROM records earlier_record
                  JOIN plugins earlier_plugin ON earlier_plugin.id = earlier_record.plugin_id
                  JOIN strings earlier_string ON earlier_string.record_id = earlier_record.id
                  WHERE earlier_record.snapshot_id = winner.snapshot_id
                    AND earlier_record.form_key = winner.form_key COLLATE NOCASE
                    AND earlier_plugin.load_order_index < winning_plugin.load_order_index
                    AND earlier_string.semantic_path = winning_string.semantic_path
                    AND earlier_string.language = 'Target'
                )
              ORDER BY winner.form_key COLLATE NOCASE
              LIMIT $take OFFSET $offset;
              """;
        command.Parameters.AddWithValue("$plugin", request.WinningPlugin is null ? DBNull.Value : request.WinningPlugin);
        command.Parameters.AddWithValue("$mod", request.SourceMod is null ? DBNull.Value : request.SourceMod);
        command.Parameters.AddWithValue("$type", request.RecordType is null ? DBNull.Value : request.RecordType);
        command.Parameters.AddWithValue("$category", request.Category is null ? DBNull.Value : request.Category);
        command.Parameters.AddWithValue("$take", request.Limit + 1);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var formKeys = new List<string>();
        while (reader.Read())
        {
            formKeys.Add(reader.GetString(0));
        }

        return CreatePage(formKeys, request.Limit, offset, scope);
    }

    public IReadOnlyList<IndexedPhysicalProvider> GetPhysicalProviders(string logicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        if (_providerCache.TryGetValue(logicalPath, out var cached))
        {
            return cached;
        }

        using var connection = OpenValidated();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT logical_path, source_kind, source_name, effective_priority, profile_line, physical_path, is_winner
            FROM physical_providers
            WHERE logical_path = $logical COLLATE NOCASE
            ORDER BY effective_priority DESC, source_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$logical", logicalPath);
        using var reader = command.ExecuteReader();
        var providers = new List<IndexedPhysicalProvider>();
        while (reader.Read())
        {
            providers.Add(new IndexedPhysicalProvider
            {
                LogicalPath = reader.GetString(0),
                SourceKind = reader.GetString(1),
                SourceName = reader.GetString(2),
                EffectivePriority = reader.GetInt64(3),
                ProfileLine = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PhysicalPath = reader.GetString(5),
                IsWinner = reader.GetBoolean(6),
            });
        }

        _providerCache[logicalPath] = providers;
        return providers;
    }

    public IndexSnapshotStatus GetStatus()
    {
        if (_statusCache is not null)
        {
            return _statusCache;
        }

        using var connection = OpenValidated();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.created_utc, s.mode, s.profile_name, s.load_order_fingerprint,
                   s.source_language, s.target_language, s.backend_name,
                   SUM(CASE WHEN p.parse_status IN ('parsed', 'partiallyParsed') THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.parse_status = 'failed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN p.parse_status = 'partiallyParsed' THEN 1 ELSE 0 END),
                   COALESCE(SUM(p.coverage_gap_record_count), 0),
                   (SELECT COUNT(*) FROM engine_game_settings e WHERE e.snapshot_id = s.id),
                   (SELECT COUNT(*) FROM post_plugin_game_settings post WHERE post.snapshot_id = s.id),
                   s.engine_game_setting_catalog_status,
                   s.engine_game_setting_catalog_path,
                   s.runtime_executable_path,
                   s.engine_game_setting_warnings
            FROM snapshots s
            LEFT JOIN plugins p ON p.snapshot_id = s.id
            WHERE s.status = 'complete'
            GROUP BY s.id
            ORDER BY s.id DESC LIMIT 1;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidDataException("The index has no complete snapshot.");
        }

        _statusCache = new IndexSnapshotStatus
        {
            SchemaVersion = SqliteSchema.Version,
            CreatedUtc = DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Mode = Enum.Parse<FalloutLoc.Core.Configuration.GameMode>(reader.GetString(1)),
            ProfileName = reader.GetString(2),
            LoadOrderFingerprint = reader.GetString(3),
            SourceLanguage = reader.GetString(4),
            TargetLanguage = reader.GetString(5),
            BackendName = reader.GetString(6),
            ParsedPlugins = reader.GetInt32(7),
            FailedPlugins = reader.GetInt32(8),
            PartiallyParsedPlugins = reader.GetInt32(9),
            CoverageGapRecords = reader.GetInt64(10),
            EngineGameSettings = reader.GetInt32(11),
            PostPluginGameSettingOverrides = reader.GetInt32(12),
            EngineGameSettingCatalogStatus = reader.GetString(13),
            EngineGameSettingCatalogPath = reader.IsDBNull(14) ? null : reader.GetString(14),
            RuntimeExecutablePath = reader.IsDBNull(15) ? null : reader.GetString(15),
            EngineGameSettingWarnings = reader.IsDBNull(16)
                ? []
                : JsonSerializer.Deserialize<string[]>(reader.GetString(16)) ?? [],
        };
        return _statusCache;
    }

    public string CheckIntegrity()
    {
        using var connection = OpenValidated();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check(1);";
        return command.ExecuteScalar() as string
            ?? throw new InvalidDataException("SQLite quick_check returned no result.");
    }

    public IndexCoverageReport GetCoverage(int issueLimit = 100)
    {
        if (issueLimit is < 1 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(issueLimit), "Issue limit must be between 1 and 10000.");
        }

        var status = GetStatus();
        using var connection = OpenValidated();
        var recordTypes = ReadCoverageRecordTypes(connection);
        var categories = ReadCoverageCategories(connection);
        var issues = ReadCoverageIssues(connection, issueLimit + 1);
        var page = issues.Take(issueLimit).ToArray();
        return new IndexCoverageReport
        {
            CatalogVersion = LocalizationFieldCatalog.Version,
            SupportedFields = LocalizationFieldCatalog.SupportedFields,
            SchemaVersion = status.SchemaVersion,
            CreatedUtc = status.CreatedUtc,
            Mode = status.Mode,
            ProfileName = status.ProfileName,
            LoadOrderFingerprint = status.LoadOrderFingerprint,
            SourceLanguage = status.SourceLanguage,
            TargetLanguage = status.TargetLanguage,
            TotalPlugins = status.ParsedPlugins + status.FailedPlugins,
            ParsedPlugins = status.ParsedPlugins - status.PartiallyParsedPlugins,
            PartiallyParsedPlugins = status.PartiallyParsedPlugins,
            FailedPlugins = status.FailedPlugins,
            TotalRecords = recordTypes.Sum(item => item.TotalRecords),
            ParsedRecords = recordTypes.Sum(item => item.ParsedRecords),
            PartiallyParsedRecords = recordTypes.Sum(item => item.PartiallyParsedRecords),
            NotApplicableRecords = recordTypes.Sum(item => item.NotApplicableRecords),
            UnverifiedRecords = recordTypes.Sum(item => item.UnverifiedRecords),
            TotalStringFields = categories.Sum(item => item.Fields),
            NonEmptyStringFields = categories.Sum(item => item.NonEmptyFields),
            AmbiguousStringFields = categories.Sum(item => item.AmbiguousFields),
            EngineGameSettings = status.EngineGameSettings,
            PostPluginGameSettingOverrides = status.PostPluginGameSettingOverrides,
            EngineGameSettingCatalogStatus = status.EngineGameSettingCatalogStatus,
            IssuesTruncated = issues.Count > issueLimit,
            RecordTypes = recordTypes,
            Categories = categories,
            Issues = page,
        };
    }

    private static IReadOnlyList<IndexCoverageRecordType> ReadCoverageRecordTypes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH string_counts AS (
                SELECT record_id, COUNT(*) AS fields,
                       SUM(CASE WHEN text IS NOT NULL AND text != '' THEN 1 ELSE 0 END) AS non_empty
                FROM strings
                GROUP BY record_id
            )
            SELECT r.record_type,
                   COUNT(*),
                   SUM(CASE WHEN r.parse_status = 'parsed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN r.parse_status = 'partiallyParsed' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN r.parse_status = 'notApplicable' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN r.parse_status = 'unverified' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN COALESCE(sc.fields, 0) > 0 THEN 1 ELSE 0 END),
                   COALESCE(SUM(sc.fields), 0),
                   COALESCE(SUM(sc.non_empty), 0)
            FROM records r
            LEFT JOIN string_counts sc ON sc.record_id = r.id
            GROUP BY r.record_type
            ORDER BY r.record_type COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<IndexCoverageRecordType>();
        while (reader.Read())
        {
            result.Add(new IndexCoverageRecordType
            {
                RecordType = reader.GetString(0),
                TotalRecords = reader.GetInt64(1),
                ParsedRecords = reader.GetInt64(2),
                PartiallyParsedRecords = reader.GetInt64(3),
                NotApplicableRecords = reader.GetInt64(4),
                UnverifiedRecords = reader.GetInt64(5),
                RecordsWithStrings = reader.GetInt64(6),
                StringFields = reader.GetInt64(7),
                NonEmptyStringFields = reader.GetInt64(8),
            });
        }

        return result;
    }

    private static IReadOnlyList<IndexCoverageCategory> ReadCoverageCategories(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT category, COUNT(*),
                   SUM(CASE WHEN text IS NOT NULL AND text != '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN language = 'Target' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN language = 'Source' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN ambiguous = 1 THEN 1 ELSE 0 END)
            FROM strings
            GROUP BY category
            ORDER BY category COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        var result = new List<IndexCoverageCategory>();
        while (reader.Read())
        {
            result.Add(new IndexCoverageCategory
            {
                Category = reader.GetString(0),
                Fields = reader.GetInt64(1),
                NonEmptyFields = reader.GetInt64(2),
                TargetFields = reader.GetInt64(3),
                SourceFields = reader.GetInt64(4),
                AmbiguousFields = reader.GetInt64(5),
            });
        }

        return result;
    }

    private static IReadOnlyList<IndexCoverageIssue> ReadCoverageIssues(SqliteConnection connection, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.name, r.form_key, r.record_type, r.editor_id, r.parse_status, r.parse_warnings
            FROM records r
            JOIN plugins p ON p.id = r.plugin_id
            WHERE r.parse_status IN ('partiallyParsed', 'unverified')
            ORDER BY r.record_type COLLATE NOCASE, p.load_order_index, r.form_key COLLATE NOCASE
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var result = new List<IndexCoverageIssue>();
        while (reader.Read())
        {
            result.Add(new IndexCoverageIssue
            {
                PluginName = reader.GetString(0),
                FormKey = reader.GetString(1),
                RecordType = reader.GetString(2),
                EditorId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = Enum.Parse<RecordParseStatus>(reader.GetString(4), ignoreCase: true),
                Warnings = reader.IsDBNull(5)
                    ? []
                    : JsonSerializer.Deserialize<string[]>(reader.GetString(5)) ?? [],
            });
        }

        return result;
    }

    private SqliteConnection OpenValidated()
    {
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Index database does not exist. Run 'faloudit index' first.", path);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT version FROM schema_info LIMIT 1),
                       (SELECT status FROM snapshots ORDER BY id DESC LIMIT 1);
                """;
            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(0) != SqliteSchema.Version || reader.GetString(1) != "complete")
            {
                throw new InvalidDataException("Index schema or snapshot status is not supported.");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static IReadOnlyList<IndexedTraceString> ReadStrings(SqliteConnection connection, long recordId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT semantic_path, category, text, language, encoding_evidence, ambiguous
            FROM strings WHERE record_id = $record ORDER BY id;
            """;
        command.Parameters.AddWithValue("$record", recordId);
        using var reader = command.ExecuteReader();
        var strings = new List<IndexedTraceString>();
        while (reader.Read())
        {
            strings.Add(new IndexedTraceString
            {
                SemanticPath = reader.GetString(0),
                Category = reader.GetString(1),
                Text = reader.IsDBNull(2) ? null : reader.GetString(2),
                Language = Enum.Parse<TextLanguageKind>(reader.GetString(3)),
                EncodingEvidence = Enum.Parse<StringEncodingEvidence>(reader.GetString(4)),
                Ambiguous = reader.GetBoolean(5),
            });
        }

        return strings;
    }

    private static IndexedPage<IndexedRecordMatch> ReadRecordPage(
        SqliteConnection connection,
        string candidatePredicate,
        Action<SqliteCommand> bindCandidateParameters,
        int limit,
        int offset,
        string scope)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH candidate_keys AS (
                SELECT DISTINCT candidate.form_key
                FROM records candidate
                JOIN plugins matched_plugin ON matched_plugin.id = candidate.plugin_id
                WHERE {candidatePredicate}
            ),
            ranked AS (
                SELECT record.form_key, record.origin_plugin, record.record_type, record.editor_id,
                       plugin.name, plugin.load_order_index, plugin.physical_path, plugin.source_mod,
                       plugin.effective_priority, record.is_deleted,
                       COUNT(*) OVER (PARTITION BY record.form_key) AS override_count,
                       ROW_NUMBER() OVER (
                           PARTITION BY record.form_key
                           ORDER BY plugin.load_order_index DESC, record.id DESC
                       ) AS winner_rank
                FROM records record
                JOIN plugins plugin ON plugin.id = record.plugin_id
                JOIN candidate_keys candidate_key
                  ON candidate_key.form_key = record.form_key COLLATE NOCASE
            )
            SELECT form_key, origin_plugin, record_type, editor_id,
                   name, load_order_index, physical_path, source_mod,
                   effective_priority, is_deleted, override_count
            FROM ranked
            WHERE winner_rank = 1
            ORDER BY form_key COLLATE NOCASE, name COLLATE NOCASE
            LIMIT $take OFFSET $offset;
            """;
        bindCandidateParameters(command);
        command.Parameters.AddWithValue("$take", limit + 1);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var results = new List<IndexedRecordMatch>();
        while (reader.Read())
        {
            results.Add(new IndexedRecordMatch
            {
                FormKey = reader.GetString(0),
                OriginPlugin = reader.GetString(1),
                RecordType = reader.GetString(2),
                EditorId = reader.IsDBNull(3) ? null : reader.GetString(3),
                WinningPluginName = reader.GetString(4),
                WinningLoadOrderIndex = reader.GetInt32(5),
                WinningPhysicalPath = reader.GetString(6),
                WinningSourceMod = reader.GetString(7),
                WinningEffectivePriority = reader.GetInt64(8),
                IsDeleted = reader.GetBoolean(9),
                OverrideCount = reader.GetInt32(10),
            });
        }

        return CreatePage(results, limit, offset, scope);
    }

    private static FormLookupInput ParseFormLookup(string input)
    {
        var value = input.Trim();
        var colon = value.IndexOf(':');
        if (colon > 0 && colon < value.Length - 1)
        {
            var local = ParseLocalFormId(value[..colon], input);
            var plugin = value[(colon + 1)..].Trim();
            if (plugin.Length == 0)
            {
                throw new ArgumentException("A FormKey must include an origin plugin after ':'.", nameof(input));
            }

            return new FormLookupInput(IndexedFormLookupKind.FormKey, local, plugin, null);
        }

        var pipe = value.LastIndexOf('|');
        if (pipe > 0 && pipe < value.Length - 1)
        {
            var plugin = value[..pipe].Trim();
            var local = ParseLocalFormId(value[(pipe + 1)..], input);
            return new FormLookupInput(IndexedFormLookupKind.FormKey, local, plugin, null);
        }

        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (hex.Length is < 1 or > 8
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var numeric))
        {
            throw new ArgumentException(
                "Form input must be a FormKey, plugin|local-id, or one to eight hexadecimal digits.",
                nameof(input));
        }

        if (hex.Length >= 7)
        {
            return new FormLookupInput(
                IndexedFormLookupKind.RuntimeFormId,
                (numeric & 0x00FF_FFFF).ToString("X6", CultureInfo.InvariantCulture),
                null,
                checked((int)(numeric >> 24)));
        }

        return new FormLookupInput(
            IndexedFormLookupKind.LocalFormId,
            numeric.ToString("X6", CultureInfo.InvariantCulture),
            null,
            null);
    }

    private static string ParseLocalFormId(string value, string input)
    {
        var hex = value.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[2..];
        }

        if (hex.Length is < 1 or > 6
            || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var local))
        {
            throw new ArgumentException($"Invalid local FormID in '{input}'.", nameof(input));
        }

        return local.ToString("X6", CultureInfo.InvariantCulture);
    }

    private static IndexedPage<T> CreatePage<T>(List<T> results, int limit, int offset, string scope)
    {
        var hasMore = results.Count > limit;
        if (hasMore)
        {
            results.RemoveAt(results.Count - 1);
        }

        return new IndexedPage<T>
        {
            Items = results,
            Limit = limit,
            HasMore = hasMore,
            NextCursor = hasMore ? EncodeCursor(checked(offset + limit), scope) : null,
        };
    }

    private static int DecodeCursor(string? cursor, string scope)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return 0;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var payload = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = payload.Split(':');
            if (parts.Length != 3
                || parts[0] != "v1"
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                || offset < 0
                || parts[2] != CursorScopeHash(scope))
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ArgumentException("The search cursor is invalid or belongs to a different query.", nameof(cursor));
        }
    }

    private static string EncodeCursor(int offset, string scope)
    {
        var payload = Encoding.UTF8.GetBytes($"v1:{offset.ToString(CultureInfo.InvariantCulture)}:{CursorScopeHash(scope)}");
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string CursorScopeHash(string scope) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..16];

    private static (string Text, int Start) BuildContentContext(
        string content,
        IndexedContentSearchRequest request,
        Regex? regex)
    {
        const int before = 240;
        const int maximumLength = 1200;
        var matchIndex = request.Mode switch
        {
            IndexedTextSearchMode.Regex => regex?.Match(content).Index ?? 0,
            _ => content.IndexOf(
                request.Query,
                request.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal),
        };
        if (matchIndex < 0)
        {
            matchIndex = 0;
        }

        var start = Math.Max(0, matchIndex - before);
        var length = Math.Min(maximumLength, content.Length - start);
        return (content.Substring(start, length), start);
    }
    private static void ValidatePageLimit(int limit)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1000.");
        }
    }

    private static string EscapeLike(string value) => value.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal);

    private sealed record FormLookupInput(
        IndexedFormLookupKind Kind,
        string LocalFormId,
        string? PluginName,
        int? RuntimeLoadOrderIndex);

    private sealed record RawOverride(
        long RecordId,
        string PluginName,
        int LoadOrderIndex,
        string PhysicalPath,
        string SourceMod,
        long EffectivePriority,
        string RecordType,
        string? EditorId,
        bool IsDeleted,
        bool IsCompressed);
}
