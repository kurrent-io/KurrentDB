using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData.ProviderServices;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// The model-driven <see cref="RecordCodec{TRecord}"/>: serves any record shape a
/// <see cref="CollectionModel"/> describes — typed POCOs and dynamic dictionary records alike —
/// with no hand-written mapping. The unconditional default when no codec is registered for the
/// record type.
/// </summary>
/// <remarks>
/// Decode reads POSITIONALLY: the connector's composers emit columns in
/// <see cref="CollectionModel.Properties"/> order (skipping vector columns on lean projections),
/// so both layouts are computed once at construction — no per-query ordinal resolution at all.
/// Multi-vector models are fully supported: each vector property is one named slot, and vectorize
/// routes PER SLOT — native extraction or MEVD's own per-property generation dispatcher.
/// </remarks>
public sealed class DuckDBModelCodec<TRecord> : RecordCodec<TRecord> where TRecord : notnull {
    readonly CollectionModel _model;

    // leanPositions[i] = property i's column position when the SELECT omits vector columns
    // (-1 for every vector property). With vectors included, position == property index.
    readonly int[] _leanPositions;

    public DuckDBModelCodec(CollectionModel model) {
        _model = model;

        var properties = model.Properties;
        _leanPositions = new int[properties.Count];

        var leanPosition = 0;

        for (var i = 0; i < properties.Count; i++)
            _leanPositions[i] = properties[i] is VectorPropertyModel ? -1 : leanPosition++;
    }

    /// <summary>
    /// Never consulted on this codec: both vectorize overrides below route per vector property
    /// (native extraction or MEVD's dispatcher), so slot texts are never gathered up front.
    /// </summary>
    protected override VectorText[] GetVectorTexts(TRecord record) =>
        throw new UnreachableException($"{nameof(DuckDBModelCodec<TRecord>)} vectorizes per vector property and never gathers slot texts.");

    public override object?[] Encode(TRecord record, VectorSlots vectors) {
        var properties = _model.Properties;
        var values     = new object?[properties.Count];

        for (var i = 0; i < properties.Count; i++) {
            // Each vector slot lands at its own property's position, looked up by STORAGE NAME —
            // the association is written at both ends, never implied by ordering. Every other value
            // passes straight through — arrays, lists, blobs and scalars all bind natively.
            values[i] = properties[i] is VectorPropertyModel vectorProperty
                ? vectors[vectorProperty.StorageName]
                : properties[i].GetValueAsObject(record);
        }

        return values;
    }

    public override TRecord Decode(DbDataReader reader, bool includeVectors) {
        var record = _model.CreateRecord<TRecord>()
                  ?? throw new InvalidOperationException($"The collection model failed to create a record of type '{typeof(TRecord)}'.");

        var properties = _model.Properties;

        for (var i = 0; i < properties.Count; i++) {
            var property = properties[i];

            if (property is VectorPropertyModel && !includeVectors) {
                // Lean projection: the column is absent; leave the vector property at its default.
                continue;
            }

            var position = includeVectors ? i : _leanPositions[i];

            if (reader.IsDBNull(position)) {
                SetNullOrLeaveDefault(record, property);
                continue;
            }

            var value = reader.GetValue(position);

            switch (property) {
                case VectorPropertyModel: property.SetValueAsObject(record, CoerceVectorFromStorage(property.Type, value)); break;

                case DataPropertyModel when TryGetCollectionElementType(property.Type, out var elementType, out var isArray):
                    property.SetValueAsObject(
                        record, isArray
                            ? CoerceToArray(elementType, value)
                            : CoerceToList(elementType, value));

                break;

                default: property.SetValueAsObject(record, CoerceScalarFromStorage(property.Type, value)); break;
            }
        }

        return record;

        // Sets the property to null for reference/nullable types; leaves the default for non-nullable value types.
        static void SetNullOrLeaveDefault(TRecord record, PropertyModel property) {
            var type = property.Type;

            if (!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
                property.SetValueAsObject(record, null);
        }

        // Coerces a vector column value (the reader hands back List<float>) to the declared CLR vector type.
        static object CoerceVectorFromStorage(Type propertyType, object value) {
            var floats     = ToFloatArray(value);
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            return targetType switch {
                Type t when t == typeof(ReadOnlyMemory<float>) => new ReadOnlyMemory<float>(floats),
                Type t when t == typeof(Embedding<float>)      => new Embedding<float>(floats),
                Type t when t == typeof(float[])               => floats,

                _ => throw new NotSupportedException($"Unsupported vector property type '{propertyType}'.")
            };
        }

        // Coerces a scalar column value to the declared CLR type (nullable-unwrapped).
        static object CoerceScalarFromStorage(Type propertyType, object value) {
            var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (targetType == typeof(DateTimeOffset)) {
                return value switch {
                    DateTimeOffset dto => dto,

                    // DuckDB TIMESTAMPTZ may surface as a DateTime; treat its clock reading as UTC.
                    DateTime dt => new(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero),

                    _ => (DateTimeOffset)Convert.ChangeType(value, typeof(DateTimeOffset), CultureInfo.InvariantCulture)
                };
            }

            if (targetType == typeof(byte[]))
                return CoerceBlobFromStorage(value);

            if (value.GetType() == targetType)
                return value;

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        // Coerces a BLOB column value to byte[]. DuckDB.NET's reader returns a populated BLOB as a
        // Stream (an UnmanagedMemoryStream), which is not IConvertible and cannot flow through the
        // Convert.ChangeType fallback — read it fully and dispose it. An empty BLOB arrives as byte[].
        static byte[] CoerceBlobFromStorage(object value) {
            switch (value) {
                case byte[] bytes: return bytes;

                case Stream stream:
                    try {
                        if (stream.CanSeek) {
                            stream.Position = 0;
                            var buffer = new byte[stream.Length];
                            var offset = 0;
                            int read;

                            while (offset                                                       < buffer.Length
                                && (read = stream.Read(buffer, offset, buffer.Length - offset)) > 0) {
                                offset += read;
                            }

                            return buffer;
                        }

                        using (var memory = new MemoryStream()) {
                            stream.CopyTo(memory);
                            return memory.ToArray();
                        }
                    } finally {
                        stream.Dispose();
                    }

                default: throw new NotSupportedException($"Unsupported BLOB value of type '{value.GetType()}'.");
            }
        }

        // Materializes value (any IEnumerable<T>) into a typed T[].
        static object CoerceToArray(Type elementType, object value) =>
            elementType switch {
                Type t when t == typeof(string) => ToArray<string>(value),
                Type t when t == typeof(bool)   => ToArray<bool>(value),
                Type t when t == typeof(short)  => ToArray<short>(value),
                Type t when t == typeof(int)    => ToArray<int>(value),
                Type t when t == typeof(long)   => ToArray<long>(value),
                Type t when t == typeof(float)  => ToArray<float>(value),
                Type t when t == typeof(double) => ToArray<double>(value),

                _ => throw new NotSupportedException($"Unsupported array element type '{elementType}'.")
            };

        // Materializes value (any IEnumerable<T>) into a typed List<T>.
        static object CoerceToList(Type elementType, object value) =>
            elementType switch {
                Type t when t == typeof(string) => ToList<string>(value),
                Type t when t == typeof(bool)   => ToList<bool>(value),
                Type t when t == typeof(short)  => ToList<short>(value),
                Type t when t == typeof(int)    => ToList<int>(value),
                Type t when t == typeof(long)   => ToList<long>(value),
                Type t when t == typeof(float)  => ToList<float>(value),
                Type t when t == typeof(double) => ToList<double>(value),

                _ => throw new NotSupportedException($"Unsupported list element type '{elementType}'.")
            };

        // A list-typed data property (T[] or List<T>), excluding byte[] (a scalar BLOB column).
        static bool TryGetCollectionElementType(Type type, [NotNullWhen(true)] out Type? elementType, out bool isArray) {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type != typeof(byte[])) {
                if (type.IsArray) {
                    elementType = type.GetElementType()!;
                    isArray     = true;
                    return true;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
                    elementType = type.GenericTypeArguments[0];
                    isArray     = false;
                    return true;
                }
            }

            elementType = null;
            isArray     = false;
            return false;
        }

        static T[] ToArray<T>(object value) => value as T[] ?? ((IEnumerable<T>)value).ToArray();

        static List<T> ToList<T>(object value) => value as List<T> ?? [.. (IEnumerable<T>)value];

        static float[] ToFloatArray(object value) => value as float[] ?? ((IEnumerable<float>)value).ToArray();
    }

    /// <summary>
    /// One slot per vector property, routed PER SLOT: a native-typed property already holds its
    /// vector — extract it, no generator; anything else is generation input, routed through MEVD's
    /// own per-property dispatcher so per-property generators and non-string inputs keep working.
    /// </summary>
    public override async ValueTask<VectorSlots> VectorizeAsync(TRecord record, CancellationToken ct = default) {
        var vectorProperties = _model.VectorProperties;

        var columns = new string[vectorProperties.Count];
        var vectors = new float[]?[vectorProperties.Count];

        for (var i = 0; i < vectorProperties.Count; i++) {
            var property = vectorProperties[i];

            // The model builder only admits a non-native vector type when a generator resolved for
            // it (definition- or store-level), so the dispatcher is guaranteed on that branch.
            columns[i] = property.StorageName;
            vectors[i] = DuckDBModelBuilder.IsVectorPropertyTypeValidCore(property.Type, out _)
                ? ExtractVector(property.GetValueAsObject(record))
                : ExtractVector(await property.GenerateEmbeddingAsync(property.GetValueAsObject(record), ct).ConfigureAwait(false));
        }

        return new(columns, vectors);
    }

    public override async ValueTask<VectorSlots[]> VectorizeBatchAsync(IReadOnlyList<TRecord> records, CancellationToken ct = default) {
        var vectorProperties = _model.VectorProperties;

        var columns = new string[vectorProperties.Count];

        // Column-major first pass: fill each property's vector for every record before moving to
        // the next property — native extraction is per record (nothing to generate), generation is
        // ONE dispatcher call per property for the WHOLE batch (the collection's upsert idiom).
        var byProperty = new float[]?[vectorProperties.Count][];

        for (var p = 0; p < vectorProperties.Count; p++) {
            var property           = vectorProperties[p];
            var vectorsForProperty = new float[]?[records.Count];

            columns[p] = property.StorageName;

            if (DuckDBModelBuilder.IsVectorPropertyTypeValidCore(property.Type, out _)) {
                for (var r = 0; r < records.Count; r++)
                    vectorsForProperty[r] = ExtractVector(property.GetValueAsObject(records[r]));
            } else {
                // Same dispatcher guarantee as VectorizeAsync above; embeddings[r] belongs to records[r].
                var embeddings = (IReadOnlyList<Embedding<float>>)await property
                    .GenerateEmbeddingsAsync(records.Select(record => property.GetValueAsObject(record)), ct)
                    .ConfigureAwait(false);

                for (var r = 0; r < records.Count; r++)
                    vectorsForProperty[r] = ExtractVector(embeddings[r]);
            }

            byProperty[p] = vectorsForProperty;
        }

        // Transpose to one VectorSlots per record, input order. The columns array is shared by
        // every slot on purpose — it is never mutated after this point.
        var slots = new VectorSlots[records.Count];

        for (var r = 0; r < records.Count; r++) {
            var vectors = new float[]?[vectorProperties.Count];

            for (var p = 0; p < vectorProperties.Count; p++)
                vectors[p] = byProperty[p][r];

            slots[r] = new(columns, vectors);
        }

        return slots;
    }

    static float[] ExtractVector(object? value) =>
        value switch {
            ReadOnlyMemory<float> memory => [.. memory.Span],
            Embedding<float> embedding   => [.. embedding.Vector.Span],
            float[] array                => array,

            null => throw new InvalidOperationException("The record's vector property is null — nothing to vectorize."),
            _    => throw new NotSupportedException($"Unsupported vector value type '{value.GetType()}'.")
        };
}
