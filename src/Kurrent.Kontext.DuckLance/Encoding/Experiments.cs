using System.Data.Common;
using Microsoft.Extensions.AI;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

public readonly record struct MyMemoryEntry(string MemoryId, string Content, string[] Tags);

/// <summary>
/// The hand-written reference codec: three sync members, zero model machinery — what a consumer
/// (or a future source generator) writes for a hot record type.
/// </summary>
public class MemoryCodec(IEmbeddingGenerator<string, Embedding<float>> embedder) : SingleVectorRecordCodec<MyMemoryEntry>(embedder) {
    // The meaning lives in the content — "Sergio lives in Norway" is what gets positioned in
    // vector space so "where does Sergio live?" lands nearby.
    protected override string GetVectorText(MyMemoryEntry record) => record.Content;

    public override object?[] Encode(MyMemoryEntry record, float[]? vector) =>
        // Model-property order: memory_id, content, tags, vec — pure placement, no conversions.
        [record.MemoryId, record.Content, record.Tags, vector];

    public override MyMemoryEntry Decode(DbDataReader reader, bool includeVectors) =>
        // Positional per the codec law; this record is vector-free, so column 3 is never read.
        new(reader.GetString(0), reader.GetString(1), [.. reader.GetFieldValue<List<string>>(2)]);
}
