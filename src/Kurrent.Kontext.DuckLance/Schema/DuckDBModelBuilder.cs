using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Builds and validates the <see cref="CollectionModel"/> for the DuckLance provider, enforcing the
/// key, data, and vector property types that the DuckDB <c>lance</c> extension can store and index.
/// </summary>
sealed class DuckDBModelBuilder() : CollectionModelBuilder(ModelBuildingOptions) {
    /// <summary>The vector CLR types supported by the DuckLance provider, used in validation error messages.</summary>
    public const string SupportedVectorTypes = "ReadOnlyMemory<float>, Embedding<float>, float[]";

    // The data CLR types supported by the DuckLance provider, used in validation error messages.
    const string SupportedDataTypes =
        "bool, short, int, long, float, double, decimal, string, DateTime, DateTimeOffset, byte[], "
      + "or arrays/lists of string, bool, short, int, long, float, double";

    static readonly CollectionModelBuildingOptions ModelBuildingOptions = new() { RequiresAtLeastOneVector = false, SupportsMultipleVectors = true };

    protected override void ValidateKeyProperty(KeyPropertyModel keyProperty) {
        base.ValidateKeyProperty(keyProperty);

        var type = keyProperty.Type;

        if (type != typeof(string))
            throw new NotSupportedException($"The property type '{type.FullName}' is not supported for key properties by the DuckLance provider. Supported types are: string.");
    }

    protected override bool IsDataPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) {
        supportedTypes = SupportedDataTypes;

        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            type = underlyingType;

        return IsScalarDataType(type)
            || (type.IsArray       && IsEnumerableElementType(type.GetElementType()!))
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && IsEnumerableElementType(type.GenericTypeArguments[0]));

        static bool IsScalarDataType(Type type) =>
            type == typeof(bool)
         || type == typeof(short)
         || type == typeof(int)
         || type == typeof(long)
         || type == typeof(float)
         || type == typeof(double)
         || type == typeof(decimal)
         || type == typeof(string)
         || type == typeof(DateTime)
         || type == typeof(DateTimeOffset)
         || type == typeof(byte[]);

        static bool IsEnumerableElementType(Type type) =>
            type == typeof(string)
         || type == typeof(bool)
         || type == typeof(short)
         || type == typeof(int)
         || type == typeof(long)
         || type == typeof(float)
         || type == typeof(double);
    }

    protected override bool IsVectorPropertyTypeValid(Type type, [NotNullWhen(false)] out string? supportedTypes) =>
        IsVectorPropertyTypeValidCore(type, out supportedTypes);

    /// <summary>Determines whether <paramref name="type"/> is a vector CLR type supported by the DuckLance provider.</summary>
    public static bool IsVectorPropertyTypeValidCore(Type type, [NotNullWhen(false)] out string? supportedTypes) {
        supportedTypes = SupportedVectorTypes;

        return type == typeof(ReadOnlyMemory<float>)
            || type == typeof(ReadOnlyMemory<float>?)
            || type == typeof(Embedding<float>)
            || type == typeof(float[]);
    }

    /// <summary>
    /// Lowercases every property's <see cref="PropertyModel.StorageName"/> and rejects models where two
    /// properties collide once normalized.
    /// </summary>
    protected override void Customize() {
        base.Customize();

        // Why lowercase at all: the DuckDB lance extension (validated against build 533e0ee) has broken
        // DELETE predicate pushdown for column names containing uppercase letters — DELETE ... WHERE Id = ?
        // silently matches ZERO rows even though the identical SELECT finds the row, so the row is never
        // removed. INSERT/MERGE/SELECT are unaffected, and quoting the identifier does not help. Default
        // storage names come from PascalCase CLR property names, so left unmitigated deletes would silently
        // fail for every default-named model. Every storage name is therefore lowercased here, unconditionally.
        //
        // Why HERE and not in ValidateProperty: the base CollectionModelBuilder pipeline calls Customize()
        // (with the full property list already populated) BEFORE Validate()/ValidateProperty(). Normalizing
        // first guarantees two things: (1) the storage-name charset gate in ValidateProperty sees the
        // already-lowercased name, so it effectively enforces ^[a-z_][a-z0-9_]*$; and (2) case-only
        // collisions (properties Id and ID, or explicit storage names "col" and "COL") are caught right here
        // — the base Validate() collision check is case-sensitive, so it would let them through and only
        // throw a less specific InvalidOperationException if the names also collided verbatim.
        var seenStorageNames = new Dictionary<string, PropertyModel>(StringComparer.Ordinal);

        foreach (var property in Properties) {
            property.StorageName = property.StorageName.ToLowerInvariant();

            if (seenStorageNames.TryGetValue(property.StorageName, out var existing)) {
                throw new ArgumentException(
                    $"Properties '{existing.ModelName}' and '{property.ModelName}' both map to the storage name "
                  + $"'{property.StorageName}' once case-normalized. The DuckLance provider lowercases all storage "
                  + "names (see DuckDBModelBuilder.Customize for why), so storage names that differ only by case "
                  + "are not allowed.");
            }

            seenStorageNames[property.StorageName] = property;
        }
    }

    protected override void ValidateProperty(PropertyModel propertyModel, VectorStoreCollectionDefinition? definition) {
        base.ValidateProperty(propertyModel, definition);

        // Note: vector dimensions < 1 are already rejected at Build time by the MEVD abstractions layer
        // (VectorStoreVectorProperty throws ArgumentOutOfRangeException "Dimensions must be greater than zero"),
        // so no duplicate dimension check is performed here.

        // Column identifiers are interpolated into SQL DDL and cannot be parameter-bound, so the storage
        // name must be a safe SQL identifier. This is a security gate against SQL injection via storage names.
        // By the time this runs, Customize() has already lowercased every storage name (see its doc comment),
        // so in practice this pattern enforces ^[a-z_][a-z0-9_]*$.
        if (!IsValidStorageName(propertyModel.StorageName))
            throw new ArgumentException(
                $"The storage name '{propertyModel.StorageName}' for property '{propertyModel.ModelName}' is not a valid column identifier. Storage names must match the pattern ^[A-Za-z_][A-Za-z0-9_]*$ (start with a letter or underscore, followed by letters, digits, or underscores).");

        // A safe SQL column identifier: ^[A-Za-z_][A-Za-z0-9_]*$.
        static bool IsValidStorageName(string storageName) {
            if (string.IsNullOrEmpty(storageName))
                return false;

            var first = storageName[0];

            if (!char.IsAsciiLetter(first) && first != '_')
                return false;

            for (var i = 1; i < storageName.Length; i++) {
                var c = storageName[i];

                if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                    return false;
            }

            return true;
        }
    }
}