using Microsoft.Extensions.AI;

namespace DuckLance.Tests.Support;

/// <summary>
/// A deterministic, call-recording <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> for the
/// codec-plumbing unit tests: every produced vector encodes (call number, position within the call),
/// so tests can assert both batching topology (ONE call, in flatten order) and that reshaping hands
/// each slot ITS OWN embedding back. This does not bend the HARD RULE (integration tests use a REAL
/// generator — see <see cref="EmbeddingModelFixture"/>): that rule proves generation end-to-end,
/// while these unit tests prove the codec's call topology, which a real model cannot make observable.
/// </summary>
public sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
    /// <summary>Every call's inputs, in call order.</summary>
    public List<string[]> Calls { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) {
        var inputs = values.ToArray();
        var call   = Calls.Count;

        Calls.Add(inputs);

        var embeddings = new List<Embedding<float>>(inputs.Length);

        for (var i = 0; i < inputs.Length; i++)
            embeddings.Add(new(new float[] { call, i }));

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
