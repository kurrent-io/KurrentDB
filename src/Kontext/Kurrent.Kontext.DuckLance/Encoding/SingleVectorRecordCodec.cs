using Microsoft.Extensions.AI;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// The single-vector tier — the 99% case: implement three sync members (<see cref="GetVectorText"/>,
/// <see cref="Encode(TRecord, float[])"/>, <see cref="RecordCodec{TRecord}.Decode"/>) and never see
/// slots, column names, or generator plumbing (see <see cref="MemoryCodec"/> for the reference).
/// Records with several vector columns extend <see cref="RecordCodec{TRecord}"/> directly and
/// address slots by name.
/// </summary>
public abstract class SingleVectorRecordCodec<TRecord>(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    : RecordCodec<TRecord>(embeddingGenerator) where TRecord : notnull {
    /// <summary>
    /// The text this record's vector is computed from — the MEANING to position in vector space
    /// (for a memory: its content, never ids or tags).
    /// </summary>
    protected abstract string GetVectorText(TRecord record);

    /// <summary>
    /// One value per column, in model-property order. Arrays and lists both bind natively — pass the
    /// record's collections straight through. The vector arrives already generated and wire-ready.
    /// </summary>
    public abstract object?[] Encode(TRecord record, float[]? vector);

    // The tier is pure routing: the record's one text travels as one anonymous slot (Single never
    // looks at names), and that slot's vector comes back out for the simple Encode. Sealed so a
    // subclass can't make the two shapes drift apart.
    protected sealed override VectorText[] GetVectorTexts(TRecord record) => [new("", GetVectorText(record))];

    public sealed override object?[] Encode(TRecord record, VectorSlots vectors) => Encode(record, vectors.Single);
}
