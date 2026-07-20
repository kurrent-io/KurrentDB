using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Connectors.Onnx;

namespace DuckLance.Tests.Support;

/// <summary>
/// Provides a shared, REAL embedding generator for tests that exercise DuckLance's write-time embedding
/// generation, backed by a small ONNX sentence-embedding model (<c>TaylorAI/bge-micro-v2</c>, 384
/// dimensions) downloaded once per process and cached under <c>~/.cache/ducklance-tests/</c>.
/// </summary>
/// <remarks>
/// <para>
/// The DuckLance provider's HARD RULE is that it never implements or fakes embedding generation itself — it
/// only ever calls through to <c>Microsoft.Extensions.AI.IEmbeddingGenerator&lt;TInput, TEmbedding&gt;</c>.
/// Tests therefore exercise it against a REAL generator rather than a hand-rolled test double. That generator
/// is obtained via the Semantic Kernel ONNX connector's own, public, non-obsolete DI bridge —
/// <c>IServiceCollection.AddBertOnnxEmbeddingGenerator(onnxModelPath, vocabPath, options)</c> — which
/// internally wraps <see cref="BertOnnxTextEmbeddingGenerationService"/> via SK's own
/// <c>ITextEmbeddingGenerationService.AsEmbeddingGenerator()</c> bridge
/// (<c>Microsoft.SemanticKernel.Embeddings.EmbeddingGenerationExtensions</c>). No hand-rolled
/// <c>IEmbeddingGenerator</c> adapter is used anywhere in this fixture: registering
/// <c>AddBertOnnxEmbeddingGenerator</c> on a plain <see cref="ServiceCollection"/> and resolving
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> from the built provider is enough, because that
/// extension method's return type is already <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>.
/// </para>
/// <para>
/// Model availability is treated the same way <see cref="LanceRequiredAttribute"/> treats platform support:
/// as an environment precondition, not a test failure. If the model is neither already cached nor
/// downloadable (e.g. the machine is offline), <see cref="IsAvailable"/> is <see langword="false"/> and
/// <see cref="Generator"/> is <see langword="null"/>; callers should skip via TUnit's dynamic
/// <c>Skip.Unless</c>/<c>Skip.When</c>, with reason <c>"embedding model unavailable (offline?)"</c>.
/// </para>
/// </remarks>
public sealed class EmbeddingModelFixture {
    /// <summary>The embedding dimensionality of the <c>bge-micro-v2</c> model.</summary>
    internal const int Dimensions = 384;

    const string ModelUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/onnx/model.onnx";
    const string VocabUrl = "https://huggingface.co/TaylorAI/bge-micro-v2/resolve/main/vocab.txt";

    static readonly SemaphoreSlim          s_gate = new(1, 1);
    static          EmbeddingModelFixture? s_instance;

    EmbeddingModelFixture(IEmbeddingGenerator<string, Embedding<float>>? generator) => Generator = generator;

    /// <summary>Gets whether a real embedding generator could be obtained (model cached or freshly downloaded).</summary>
    [MemberNotNullWhen(true, nameof(Generator))]
    public bool IsAvailable => Generator is not null;

    /// <summary>Gets the real, SK-provided, ONNX-backed embedding generator, or <see langword="null"/> when <see cref="IsAvailable"/> is <see langword="false"/>.</summary>
    public IEmbeddingGenerator<string, Embedding<float>>? Generator { get; }

    /// <summary>
    /// Gets the shared fixture instance, downloading and caching the model (and constructing the generator)
    /// on the first call only. Safe to call repeatedly and concurrently from any number of tests.
    /// </summary>
    public static async Task<EmbeddingModelFixture> GetAsync(CancellationToken cancellationToken = default) {
        if (s_instance is not null)
            return s_instance;

        await s_gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            return s_instance ??= await CreateAsync(cancellationToken).ConfigureAwait(false);
        } finally {
            s_gate.Release();
        }
    }

    static async Task<EmbeddingModelFixture> CreateAsync(CancellationToken cancellationToken) {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "ducklance-tests",
            "bge-micro-v2");

        Directory.CreateDirectory(cacheDir);

        var modelPath = Path.Combine(cacheDir, "model.onnx");
        var vocabPath = Path.Combine(cacheDir, "vocab.txt");

        var ready = File.Exists(modelPath) && File.Exists(vocabPath);

        if (!ready)
            ready = await TryDownloadAsync(modelPath, vocabPath, cancellationToken).ConfigureAwait(false);

        if (!ready)
            return new(generator: null);

        try {
            var services = new ServiceCollection();
            services.AddBertOnnxEmbeddingGenerator(modelPath, vocabPath, new());

            using var provider = services.BuildServiceProvider();

            var generator =
                provider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            return new(generator);
        } catch (Exception ex) when (ex is IOException or InvalidOperationException) {
            // A cached-but-corrupt model/vocab pair (e.g. a truncated prior download) fails to load; treat
            // that the same as "unavailable" rather than failing every test in the process.
            return new(generator: null);
        }
    }

    /// <summary>
    /// Downloads the model and vocab files to <paramref name="modelPath"/>/<paramref name="vocabPath"/>,
    /// via a temporary file plus rename so a failed/interrupted download never leaves a corrupt file behind
    /// for a later run to mistake for a valid cache hit.
    /// </summary>
    static async Task<bool> TryDownloadAsync(string modelPath, string vocabPath, CancellationToken cancellationToken) {
        try {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

            await DownloadToAsync(
                    httpClient, ModelUrl, modelPath,
                    cancellationToken)
                .ConfigureAwait(false);

            await DownloadToAsync(
                    httpClient, VocabUrl, vocabPath,
                    cancellationToken)
                .ConfigureAwait(false);

            return true;
        } catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or UnauthorizedAccessException or TaskCanceledException) {
            // Offline-tolerant by design: no network, DNS failure, timeout, or a read-only cache directory
            // all just mean "the model isn't available in this environment" — not a test infrastructure error.
            TryDelete(modelPath);
            TryDelete(vocabPath);
            return false;
        }
    }

    static async Task DownloadToAsync(HttpClient httpClient, string url, string destinationPath, CancellationToken cancellationToken) {
        var tempPath = destinationPath + ".download";

        using (var responseStream = await httpClient.GetStreamAsync(new Uri(url), cancellationToken).ConfigureAwait(false))
        using (var fileStream = File.Create(tempPath)) {
            await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, destinationPath, true);
    }

    static void TryDelete(string path) {
        try {
            if (File.Exists(path))
                File.Delete(path);
        } catch (IOException) {
            // Best-effort cleanup.
        } catch (UnauthorizedAccessException) {
            // Best-effort cleanup.
        }
    }
}