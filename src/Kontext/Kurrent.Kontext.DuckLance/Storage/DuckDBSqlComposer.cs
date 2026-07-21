using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Composes the DuckDB SQL statements used by <see cref="DuckDBCollection{TKey, TRecord}"/> for its
/// CRUD operations (upsert, get-by-key, delete-by-key), from a validated <see cref="CollectionModel"/>.
/// </summary>
static class DuckDBSqlComposer {
    /// <summary>Builds a single-row <c>MERGE</c> upsert statement, keyed on the model's key property.</summary>
    public static string BuildMergeUpsertSql(string qualifiedTableName, CollectionModel model) {
        // One positional (?) parameter per property, in CollectionModel.Properties order — the exact order
        // the record codec's Encode emits values in (the codec law). Column identifiers are interpolated
        // directly (the model builder validated them as safe SQL identifiers); only values are parameter-bound.
        // No trailing semicolon.
        var properties = model.Properties;
        var keyColumn  = model.KeyProperty.StorageName;

        var usingItems       = new List<string>(properties.Count);
        var allColumns       = new List<string>(properties.Count);
        var allSourceColumns = new List<string>(properties.Count);
        var setClauses       = new List<string>(properties.Count);

        foreach (var property in properties) {
            var column = property.StorageName;

            usingItems.Add(BuildUsingSelectItem(property));
            allColumns.Add(column);
            allSourceColumns.Add($"s.{column}");

            if (property is not KeyPropertyModel)
                setClauses.Add($"{column} = s.{column}");
        }

        return
            $"""
             MERGE INTO {qualifiedTableName} AS t
             USING (SELECT {string.Join(", ", usingItems)}) AS s
             ON t.{keyColumn} = s.{keyColumn}
             WHEN MATCHED THEN UPDATE SET {string.Join(", ", setClauses)}
             WHEN NOT MATCHED THEN INSERT ({string.Join(", ", allColumns)}) VALUES ({string.Join(", ", allSourceColumns)})
             """;

        // Builds the USING-clause projection item for one property: "? AS {column}" for keys and scalars, or
        // "CAST(? AS ...) AS {column}" for vectors and list-typed data properties (so DuckDB binds the correct
        // array / fixed-size-array element type; scalars are inferred from the bound value).
        static string BuildUsingSelectItem(PropertyModel property) =>
            property switch {
                VectorPropertyModel vector => $"CAST(? AS FLOAT[{vector.Dimensions}]) AS {property.StorageName}",
                DataPropertyModel data when IsListTypedDataProperty(data)
                    => $"CAST(? AS {DuckDBSchemaBuilder.GetDuckDbType(data)}) AS {property.StorageName}",
                _ => $"? AS {property.StorageName}"
            };
    }

    /// <summary>
    /// Builds a multi-row <c>MERGE</c> upsert statement for <c>rowCount</c> records — the batch counterpart to
    /// <see cref="BuildMergeUpsertSql"/> — whose source is a single <c>USING (VALUES ...) AS s ({col list})</c>.
    /// </summary>
    public static string BuildMergeUpsertManySql(string qualifiedTableName, CollectionModel model, int rowCount) {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowCount, 1);

        // Collapsing an N-record batch into ONE multi-row MERGE matters twice over. Every Lance write is an
        // atomic commit, so one statement means one commit instead of N — shrinking the window in which a
        // concurrent writer can conflict. And the engine plans a single statement, replacing the previous
        // serial per-record loop, whose cost grew O(n²) as the dataset grew.
        //
        // Parameters are bound row-major: all of record 0's values in property order, then record 1's, ... —
        // rowCount × property-count placeholders in total.
        //
        // The CALLER owns two preconditions:
        // - De-duplicate keys within the batch (keeping the last occurrence) BEFORE calling. DuckDB's MERGE
        //   evaluates all source rows against the PRE-merge target, so two source rows sharing a key that is
        //   absent from the target both take the WHEN NOT MATCHED branch and are both inserted — two rows
        //   with the same key.
        // - Chunk very large batches (bounding the row count per statement) to keep the generated SQL and
        //   the parameter count in check.
        var properties = model.Properties;
        var keyColumn  = model.KeyProperty.StorageName;

        var allColumns          = new List<string>(properties.Count);
        var insertSourceColumns = new List<string>(properties.Count);
        var setClauses          = new List<string>(properties.Count);

        foreach (var property in properties) {
            var column = property.StorageName;

            allColumns.Add(column);
            insertSourceColumns.Add($"s.{column}");

            if (property is not KeyPropertyModel)
                setClauses.Add($"{column} = s.{column}");
        }

        // One shared row template — the placeholders differ per row only in their bound values, never in
        // shape. Vector and list-typed data properties carry their CAST(? AS ...) on EVERY row (not just the
        // first) so DuckDB binds the correct fixed-size-array / array element type regardless of which rows
        // carry null — mirroring the single-row shape's per-placeholder casts.
        var rowTemplate = "(" + string.Join(", ", properties.Select(BuildValuesRowItem)) + ")";
        var columnList  = string.Join(", ", allColumns);

        return
            $"""
             MERGE INTO {qualifiedTableName} AS t
             USING (VALUES {string.Join(", ", Enumerable.Repeat(rowTemplate, rowCount))}) AS s ({columnList})
             ON t.{keyColumn} = s.{keyColumn}
             WHEN MATCHED THEN UPDATE SET {string.Join(", ", setClauses)}
             WHEN NOT MATCHED THEN INSERT ({columnList}) VALUES ({string.Join(", ", insertSourceColumns)})
             """;

        // Builds one VALUES-row placeholder (the multi-row counterpart of BuildUsingSelectItem); column
        // identity comes from the "AS s ({col list})" alias, so no per-item "AS {column}" is emitted.
        static string BuildValuesRowItem(PropertyModel property) =>
            property switch {
                VectorPropertyModel vector => $"CAST(? AS FLOAT[{vector.Dimensions}])",
                DataPropertyModel data when IsListTypedDataProperty(data)
                    => $"CAST(? AS {DuckDBSchemaBuilder.GetDuckDbType(data)})",
                _ => "?"
            };
    }

    /// <summary>Builds a <c>SELECT ... WHERE {key} = ?</c> statement that reads a single record by key.</summary>
    public static string BuildSelectByKeySql(string qualifiedTableName, CollectionModel model, bool includeVectors) =>
        $"SELECT {BuildColumnList(model, includeVectors)} FROM {qualifiedTableName} WHERE {model.KeyProperty.StorageName} = ?";

    /// <summary>Builds a <c>SELECT ... WHERE {key} IN (?, ?, ...)</c> statement that reads multiple records by key.</summary>
    public static string BuildSelectByKeysSql(string qualifiedTableName, CollectionModel model, bool includeVectors, int keyCount) =>
        $"SELECT {BuildColumnList(model, includeVectors)} FROM {qualifiedTableName} WHERE {model.KeyProperty.StorageName} IN ({BuildPlaceholders(keyCount)})";

    /// <summary>Builds a <c>DELETE ... WHERE {key} = ?</c> statement that deletes a single record by key.</summary>
    public static string BuildDeleteByKeySql(string qualifiedTableName, CollectionModel model) => $"DELETE FROM {qualifiedTableName} WHERE {model.KeyProperty.StorageName} = ?";

    /// <summary>Builds a <c>DELETE ... WHERE {key} IN (?, ?, ...)</c> statement that deletes multiple records by key.</summary>
    public static string BuildDeleteByKeysSql(string qualifiedTableName, CollectionModel model, int keyCount) =>
        $"DELETE FROM {qualifiedTableName} WHERE {model.KeyProperty.StorageName} IN ({BuildPlaceholders(keyCount)})";

    /// <summary>A list-typed data property (<c>T[]</c> or <c>List&lt;T&gt;</c>), excluding <c>byte[]</c> (a scalar BLOB column).</summary>
    static bool IsListTypedDataProperty(DataPropertyModel property) {
        var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;

        if (type == typeof(byte[]))
            return false;

        return type.IsArray
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));
    }

    /// <summary>
    /// Builds the comma-separated <c>SELECT</c> column list in model property order, omitting vector columns
    /// when <c>includeVectors</c> is <see langword="false"/>.
    /// </summary>
    /// <summary>The SELECT column list in model-property order (vector columns omitted on lean reads) — the codec law's read side.</summary>
    public static string BuildColumnList(CollectionModel model, bool includeVectors) {
        var columns = new List<string>(model.Properties.Count);

        foreach (var property in model.Properties) {
            if (property is VectorPropertyModel && !includeVectors)
                continue;

            columns.Add(property.StorageName);
        }

        return string.Join(", ", columns);
    }

    /// <summary>Builds a comma-separated list of <paramref name="count"/> positional (<c>?</c>) placeholders.</summary>
    static string BuildPlaceholders(int count) => string.Join(", ", Enumerable.Repeat("?", count));
}