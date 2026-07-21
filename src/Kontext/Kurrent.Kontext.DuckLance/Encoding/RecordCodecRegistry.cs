namespace Kurrent.SemanticKernel.Connectors.DuckLance;

/// <summary>
/// Per-record-type codec registrations for a vector store: a registered codec wins, every other
/// record type falls back to the model-driven <see cref="DuckDBModelCodec{TRecord}"/> — resolution
/// never fails and never asks.
/// </summary>
public sealed class RecordCodecRegistry {
    // One codec per record CLR type; the value is always the RecordCodec<TRecord> of its key, so
    // the cast in Resolve is safe by construction.
    readonly Dictionary<Type, object> _codecs;

    public RecordCodecRegistry() => _codecs = [];

    // The defensive copy the options copy-ctor takes: same registrations, detached dictionary.
    internal RecordCodecRegistry(RecordCodecRegistry source) => _codecs = new(source._codecs);

    /// <summary>Registers (or replaces) the codec for <typeparamref name="TRecord"/>.</summary>
    public void Add<TRecord>(RecordCodec<TRecord> codec) where TRecord : notnull => _codecs[typeof(TRecord)] = codec;

    /// <summary>The registered codec for <typeparamref name="TRecord"/>, or null — the collection then falls back to the model codec.</summary>
    internal RecordCodec<TRecord>? Resolve<TRecord>() where TRecord : notnull =>
        _codecs.TryGetValue(typeof(TRecord), out var codec) ? (RecordCodec<TRecord>)codec : null;
}
