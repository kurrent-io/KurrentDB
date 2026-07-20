using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Builds the <c>CREATE INDEX</c> DDL statements DuckLance issues against a Lance dataset's raw filesystem
/// path, and parses the result of <c>SHOW INDEXES</c> back into a structured shape.
/// </summary>
static class DuckDBIndexBuilder {
    // Index DDL targets the raw dataset path (e.g. {storagePath}/{tableName}.lance), never the attached
    // Lance namespace name — index DDL cannot address the qualified name:
    //     CREATE INDEX {name} ON '{datasetPath}' ({column}) USING {TYPE} WITH (key = value, ...)
    // Inside the WITH clause, options always use '=' — never Lance's alternate ':=' syntax, which this
    // extension version rejects with "unknown field".

    /// <summary>
    /// Builds the <c>CREATE INDEX ... USING {type}</c> statement for a vector property, or <see langword="null"/>
    /// when the property's index kind maps to no index (brute-force flat search).
    /// </summary>
    public static string? BuildVectorIndexSql(string datasetPath, VectorPropertyModel vectorProperty) {
        var type = DuckDBIndexKindMapper.GetLanceIndexType(vectorProperty.IndexKind);

        if (type is null)
            return null;

        var metric = DuckDBIndexKindMapper.GetLanceMetric(vectorProperty.DistanceFunction);
        var column = vectorProperty.StorageName;

        var options = new List<string> { $"metric_type = '{metric}'", "num_partitions = 1" };

        if (DuckDBIndexKindMapper.IsPqFamily(vectorProperty.IndexKind)) {
            var numSubVectors = DuckDBIndexKindMapper.GetNumSubVectors(vectorProperty.Dimensions);

            // Unreachable given GetNumSubVectors' divisor rule, but guarded defensively before any SQL is
            // issued — Lance errors when dimensions % num_sub_vectors != 0.
            if (vectorProperty.Dimensions % numSubVectors != 0) {
                throw new ArgumentException(
                    $"The vector property '{vectorProperty.ModelName}' has {vectorProperty.Dimensions} dimensions, "
                  + $"which is not evenly divisible by the computed num_sub_vectors ({numSubVectors}).",
                    nameof(vectorProperty));
            }

            options.Add($"num_sub_vectors = {numSubVectors.ToString(CultureInfo.InvariantCulture)}");
            options.Add("num_bits = 8");
        }

        if (string.Equals(type, "IVF_HNSW_PQ", StringComparison.Ordinal)) {
            options.Add("hnsw_m = 16");
            options.Add("hnsw_ef_construction = 100");
        }

        return $"CREATE INDEX {column}_idx ON '{EscapePath(datasetPath)}' ({column}) USING {type} WITH ({string.Join(", ", options)})";
    }

    /// <summary>
    /// Builds the scalar <c>CREATE INDEX</c> statement(s) for a data property: an <c>INVERTED</c> full-text
    /// index when full-text indexed, and/or a <c>LABEL_LIST</c>/<c>BTREE</c> index when indexed (a property
    /// may be both, yielding both statements).
    /// </summary>
    public static IEnumerable<string> BuildScalarIndexSqls(string datasetPath, DataPropertyModel property) {
        var escapedPath = EscapePath(datasetPath);
        var column      = property.StorageName;

        if (property.IsFullTextIndexed)
            yield return $"CREATE INDEX {column}_fts_idx ON '{escapedPath}' ({column}) USING INVERTED";

        if (property.IsIndexed) {
            var indexType = IsStringListType(property.Type) ? "LABEL_LIST" : "BTREE";
            yield return $"CREATE INDEX {column}_idx ON '{escapedPath}' ({column}) USING {indexType}";
        }

        static bool IsStringListType(Type type) => type == typeof(string[]) || type == typeof(List<string>);
    }

    /// <summary>
    /// Parses the result of <c>SHOW INDEXES ON '{datasetPath}'</c>, whose validated shape is exactly the
    /// columns <c>index_name, index_type, fields, rows_indexed, details</c> (<c>details</c> is ignored).
    /// </summary>
    public static IReadOnlyList<(string Name, string Type, string Fields, long RowsIndexed)> ParseShowIndexes(DbDataReader reader) {
        var results = new List<(string Name, string Type, string Fields, long RowsIndexed)>();

        var nameOrdinal        = reader.GetOrdinal("index_name");
        var typeOrdinal        = reader.GetOrdinal("index_type");
        var fieldsOrdinal      = reader.GetOrdinal("fields");
        var rowsIndexedOrdinal = reader.GetOrdinal("rows_indexed");

        while (reader.Read()) {
            var name   = reader.GetString(nameOrdinal);
            var type   = reader.GetString(typeOrdinal);
            var fields = reader.GetString(fieldsOrdinal);

            var rowsIndexed = reader.IsDBNull(rowsIndexedOrdinal)
                ? 0L
                : Convert.ToInt64(reader.GetValue(rowsIndexedOrdinal), CultureInfo.InvariantCulture);

            results.Add((name, type, fields, rowsIndexed));
        }

        return results;
    }

    /// <summary>Escapes a filesystem path for interpolation into a single-quoted SQL string literal.</summary>
    static string EscapePath(string datasetPath) => datasetPath.Replace("'", "''", StringComparison.Ordinal);
}