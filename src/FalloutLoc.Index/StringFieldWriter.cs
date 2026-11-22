using FalloutLoc.Backends.Models;
using Microsoft.Data.Sqlite;

namespace FalloutLoc.Index;

// Both tables share the original insertion order. The allocator and dictionary are
// transaction-local so failed/removed plugins cannot leave stale IDs in memory.
internal sealed class StringFieldWriter : IDisposable
{
    private readonly RowInsert values;
    private readonly RowInsert empty;
    private readonly RowInsert shapes;
    private readonly Dictionary<(string Path, string Category), long> shapeIds = new();
    private long lastFieldId;
    private long lastShapeId;

    public StringFieldWriter(SqliteTransaction transaction)
    {
        using var query = transaction.Connection!.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT max(coalesce((SELECT max(id) FROM string_values),0),
                       coalesce((SELECT max(id) FROM empty_fields),0));
            """;
        lastFieldId = (long)query.ExecuteScalar()!;
        query.CommandText = "SELECT id, semantic_path, category FROM empty_field_shapes;";
        using (var reader = query.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                shapeIds.Add((reader.GetString(1), reader.GetString(2)), id);
                lastShapeId = Math.Max(lastShapeId, id);
            }
        }
        shapes = new(transaction, "INSERT INTO empty_field_shapes(id,semantic_path,category) VALUES (?1,?2,?3);");
        empty = new(transaction, "INSERT INTO empty_fields(id,record_id,shape_id) VALUES (?1,?2,?3);");
        values = new(transaction, """
            INSERT INTO string_values(id,record_id,semantic_path,category,text,normalized_text,language,
                                      encoding_evidence,bytes_sha256,ambiguous)
            VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10);
            """);
    }

    public void Insert(long recordId, RecordStringOccurrence field)
    {
        var id = checked(++lastFieldId);
        if (field is { Text: null, Language: TextLanguageKind.Empty, EncodingEvidence: StringEncodingEvidence.None,
            RecoveredBytesSha256: null, Ambiguous: false })
        {
            var key = (field.SemanticPath, field.Category);
            if (!shapeIds.TryGetValue(key, out var shape))
            {
                shape = checked(++lastShapeId);
                shapes.Bind(1, shape);
                shapes.Bind(2, field.SemanticPath);
                shapes.Bind(3, field.Category);
                shapes.Execute();
                shapeIds.Add(key, shape);
            }
            empty.Bind(1, id);
            empty.Bind(2, recordId);
            empty.Bind(3, shape);
            empty.Execute();
            return;
        }
        values.Bind(1, id);
        values.Bind(2, recordId);
        values.Bind(3, field.SemanticPath);
        values.Bind(4, field.Category);
        values.Bind(5, field.Text);
        values.Bind(6, TextNormalizer.Normalize(field.Text));
        values.Bind(7, field.Language.ToString());
        values.Bind(8, field.EncodingEvidence.ToString());
        values.Bind(9, field.RecoveredBytesSha256);
        values.Bind(10, field.Ambiguous ? 1 : 0);
        values.Execute();
    }

    public void Dispose()
    {
        values.Dispose();
        empty.Dispose();
        shapes.Dispose();
    }
}
