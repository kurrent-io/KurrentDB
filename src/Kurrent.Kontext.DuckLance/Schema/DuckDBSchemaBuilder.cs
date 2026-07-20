using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Builds DuckDB DDL statements from a <see cref="CollectionModel"/>, and maps individual
/// <see cref="PropertyModel"/> instances to their corresponding DuckDB column type.
/// </summary>
static class DuckDBSchemaBuilder {
    /// <summary>
    /// Builds a <c>CREATE TABLE IF NOT EXISTS</c> statement whose columns are the model's properties, in
    /// <see cref="CollectionModel.Properties"/> order.
    /// </summary>
    public static string BuildCreateTableSql(string qualifiedTableName, CollectionModel model) =>
        $"CREATE TABLE IF NOT EXISTS {qualifiedTableName} ({string.Join(", ", model.Properties.Select(property => $"{property.StorageName} {GetDuckDbType(property)}"))})";

    /// <summary>Maps a <see cref="PropertyModel"/> to its DuckDB column type.</summary>
    public static string GetDuckDbType(PropertyModel property) {
        return property switch {
            KeyPropertyModel                   => "VARCHAR",
            VectorPropertyModel vectorProperty => $"FLOAT[{vectorProperty.Dimensions}]",
            DataPropertyModel dataProperty     => GetDuckDbTypeForDataProperty(dataProperty),

            // The model builder validates property types before this layer is ever reached.
            _ => throw new UnreachableException($"Unsupported property model type '{property.GetType()}' for property '{property.ModelName}'.")
        };

        static string GetDuckDbTypeForDataProperty(DataPropertyModel property) {
            var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;

            if (TryGetScalarDuckDbType(type, out var scalarDuckDbType))
                return scalarDuckDbType;

            if (type.IsArray && TryGetCollectionElementDuckDbType(type.GetElementType()!, out var arrayElementDuckDbType))
                return $"{arrayElementDuckDbType}[]";

            if (type.IsGenericType
             && type.GetGenericTypeDefinition() == typeof(List<>)
             && TryGetCollectionElementDuckDbType(type.GetGenericArguments()[0], out var listElementDuckDbType))
                return $"{listElementDuckDbType}[]";

            // The model builder validates property types before this layer is ever reached.
            throw new UnreachableException($"Unsupported data property type '{property.Type}' for property '{property.ModelName}'.");
        }

        static bool TryGetScalarDuckDbType(Type type, [NotNullWhen(true)] out string? duckDbType) {
            duckDbType = type switch {
                Type t when t == typeof(string)         => "VARCHAR",
                Type t when t == typeof(bool)           => "BOOLEAN",
                Type t when t == typeof(short)          => "SMALLINT",
                Type t when t == typeof(int)            => "INTEGER",
                Type t when t == typeof(long)           => "BIGINT",
                Type t when t == typeof(float)          => "FLOAT",
                Type t when t == typeof(double)         => "DOUBLE",
                Type t when t == typeof(decimal)        => "DECIMAL(38,18)",
                Type t when t == typeof(DateTime)       => "TIMESTAMP",
                Type t when t == typeof(DateTimeOffset) => "TIMESTAMP WITH TIME ZONE",
                Type t when t == typeof(byte[])         => "BLOB",

                _ => null
            };

            return duckDbType is not null;
        }

        static bool TryGetCollectionElementDuckDbType(Type elementType, [NotNullWhen(true)] out string? duckDbType) {
            duckDbType = elementType switch {
                Type t when t == typeof(string) => "VARCHAR",
                Type t when t == typeof(bool)   => "BOOLEAN",
                Type t when t == typeof(short)  => "SMALLINT",
                Type t when t == typeof(int)    => "INTEGER",
                Type t when t == typeof(long)   => "BIGINT",
                Type t when t == typeof(float)  => "FLOAT",
                Type t when t == typeof(double) => "DOUBLE",

                _ => null
            };

            return duckDbType is not null;
        }
    }
}