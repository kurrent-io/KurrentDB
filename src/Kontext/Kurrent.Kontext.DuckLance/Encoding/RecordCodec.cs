using System.Data.Common;
using Microsoft.Extensions.AI;

namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// The extension point for record ↔ storage mapping. A record has one vector SLOT per vector
/// column, addressed by storage column name at both ends (<see cref="GetVectorTexts"/> and
/// <see cref="Encode"/>) — never by position. Most record types have exactly one vector column:
/// extend <see cref="SingleVectorRecordCodec{TRecord}"/> for those and never see slots at all.
/// The base owns all embedding-generator interaction — single and batch.
/// </summary>
/// <remarks>
/// THE LAW, in both directions: columns are always in model-property order — <see cref="Encode"/>'s
/// values[i] IS column i, and <see cref="Decode"/> reads by position under the same rule. The
/// connector composes all of its own SQL from the model, so positions are stable by construction.
/// Getting order wrong does not throw — it writes wrong data.
/// </remarks>
public abstract class RecordCodec<TRecord>(IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    where TRecord : notnull {
    protected IEmbeddingGenerator<string, Embedding<float>>? EmbeddingGenerator { get; } = embeddingGenerator;

    /// <summary>
    /// One entry per vector column: which storage column, and the text whose MEANING that column's
    /// vector positions in vector space (for a memory: its content, never ids or tags).
    /// </summary>
    protected abstract VectorText[] GetVectorTexts(TRecord record);

    /// <summary>
    /// One value per column, in model-property order — each vector slot placed at its own column's
    /// position, taken from <paramref name="vectors"/> BY NAME. Arrays and lists both bind natively —
    /// pass the record's collections straight through. The vectors arrive already generated and wire-ready.
    /// </summary>
    public abstract object?[] Encode(TRecord record, VectorSlots vectors);

    /// <summary>
    /// Reads THE CURRENT ROW into a record — one entry, no session, no state; the caller drives the
    /// loop. When <paramref name="includeVectors"/> is <see langword="false"/> the SELECT omitted the
    /// vector columns, so later columns sit at shifted positions.
    /// </summary>
    public abstract TRecord Decode(DbDataReader reader, bool includeVectors);

    /// <summary>
    /// Embeds one record — the primary mode: ONE generator call covering every slot, wire-ready
    /// named output. Override when vectors don't come from text (precomputed/imported vectors, image
    /// or audio encoders, …); <see cref="GetVectorTexts"/> is then unused and no generator is required.
    /// </summary>
    public virtual async ValueTask<VectorSlots> VectorizeAsync(TRecord record, CancellationToken ct = default) {
        if (EmbeddingGenerator is null)
            throw new InvalidOperationException($"{GetType().Name} has no embedding generator — supply one or override VectorizeAsync.");

        var texts  = GetVectorTexts(record);
        var inputs = new string[texts.Length];

        for (var i = 0; i < texts.Length; i++)
            inputs[i] = texts[i].Text;

        var embeddings = await EmbeddingGenerator.GenerateAsync(inputs, cancellationToken: ct).ConfigureAwait(false);

        var columns = new string[texts.Length];
        var vectors = new float[]?[texts.Length];

        for (var i = 0; i < texts.Length; i++) {
            columns[i] = texts[i].Column;
            vectors[i] = ToWireVector(embeddings[i]);
        }

        return new(columns, vectors);

        // Converts an embedding to the wire shape the vector column binds as.
        static float[] ToWireVector(Embedding<float> embedding) => [.. embedding.Vector.Span];
    }

    /// <summary>
    /// Embeds a whole batch with ONE generator call (slots[i] belongs to records[i]) no matter how
    /// many slots each record carries — free for every codec because the base knows every record's
    /// texts. Degrading to a call per record (or per slot) is a regression.
    /// </summary>
    public virtual async ValueTask<VectorSlots[]> VectorizeBatchAsync(IReadOnlyList<TRecord> records, CancellationToken ct = default) {
        if (EmbeddingGenerator is null)
            throw new InvalidOperationException($"{GetType().Name} has no embedding generator — supply one or override VectorizeBatchAsync.");

        // Flatten every record's slot texts into one flat list: record r's slots sit contiguously,
        // right after all of record r-1's slots — so the whole batch costs one generator call.
        var perRecord = new VectorText[records.Count][];
        var flatTexts = new List<string>();

        for (var r = 0; r < records.Count; r++) {
            perRecord[r] = GetVectorTexts(records[r]);

            foreach (var text in perRecord[r])
                flatTexts.Add(text.Text);
        }

        var embeddings = await EmbeddingGenerator.GenerateAsync(flatTexts, cancellationToken: ct).ConfigureAwait(false);

        // Reshape the flat result back into one VectorSlots per record, in input order — the same
        // walk as the flatten above, so embedding next always belongs to the slot being filled.
        var slots = new VectorSlots[records.Count];
        var next  = 0;

        for (var r = 0; r < records.Count; r++) {
            var texts   = perRecord[r];
            var columns = new string[texts.Length];
            var vectors = new float[]?[texts.Length];

            for (var s = 0; s < texts.Length; s++) {
                columns[s] = texts[s].Column;
                vectors[s] = ToWireVector(embeddings[next++]);
            }

            slots[r] = new(columns, vectors);
        }

        return slots;

        // Converts an embedding to the wire shape the vector column binds as.
        static float[] ToWireVector(Embedding<float> embedding) => [.. embedding.Vector.Span];
    }
}
