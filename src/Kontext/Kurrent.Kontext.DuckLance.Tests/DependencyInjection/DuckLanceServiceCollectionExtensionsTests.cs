using DuckLance.Tests.Support;
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace DuckLance.Tests.DependencyInjection;

/// <summary>
/// Unit tests for <see cref="DuckLanceServiceCollectionExtensions"/>. Every test here is construction-only: it
/// registers services, resolves them, and asserts on wiring/lifetime/options-flow — no test opens a real
/// DuckDB/Lance connection (constructing a <see cref="DuckDBVectorStore"/>, and even resolving a
/// <see cref="DuckDBCollection{TKey, TRecord}"/> via <see cref="VectorStore.GetCollection{TKey, TRecord}"/>, never
/// itself connects; connections are opened lazily on first actual query). None of these tests are therefore gated
/// by <see cref="LanceRequiredAttribute"/>.
/// </summary>
public class DuckLanceServiceCollectionExtensionsTests {
    // --- Resolves both DuckDBVectorStore and VectorStore, as the same instance ---

    [Test]
    public async Task AddDuckLanceVectorStore_ResolvesStoreAndVectorStore_AsSameInstance() {
        var services = new ServiceCollection();
        services.AddDuckLanceVectorStore(NewOptions());

        using var provider = services.BuildServiceProvider();

        using var store       = provider.GetRequiredService<DuckDBVectorStore>();
        var       vectorStore = provider.GetRequiredService<VectorStore>();

        await Assert.That(store).IsNotNull();
        await Assert.That(ReferenceEquals(store, vectorStore)).IsTrue();
    }

    // --- Keyed resolves by key; unkeyed resolution is absent ---

    [Test]
    public async Task AddKeyedDuckLanceVectorStore_ResolvesByKey_AndUnkeyedIsAbsent() {
        var services = new ServiceCollection();
        services.AddKeyedDuckLanceVectorStore("mykey", NewOptions());

        using var provider = services.BuildServiceProvider();

        using var keyedStore       = provider.GetRequiredKeyedService<DuckDBVectorStore>("mykey");
        var       keyedVectorStore = provider.GetRequiredKeyedService<VectorStore>("mykey");

        await Assert.That(keyedStore).IsNotNull();
        await Assert.That(ReferenceEquals(keyedStore, keyedVectorStore)).IsTrue();

        await Assert.That(provider.GetService<DuckDBVectorStore>()).IsNull();
        await Assert.That(provider.GetService<VectorStore>()).IsNull();
    }

    // --- ServiceLifetime is respected ---

    [Test]
    public async Task AddDuckLanceVectorStore_SingletonLifetime_ReturnsSameInstanceAcrossResolutions() {
        var services = new ServiceCollection();
        services.AddDuckLanceVectorStore(NewOptions());

        using var provider = services.BuildServiceProvider();

        var first  = provider.GetRequiredService<DuckDBVectorStore>();
        var second = provider.GetRequiredService<DuckDBVectorStore>();

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task AddDuckLanceVectorStore_TransientLifetime_ReturnsDifferentInstancesAcrossResolutions() {
        var services = new ServiceCollection();
        services.AddDuckLanceVectorStore(NewOptions(), ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();

        using var first  = provider.GetRequiredService<DuckDBVectorStore>();
        using var second = provider.GetRequiredService<DuckDBVectorStore>();

        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    // --- Options-provider overload is invoked lazily, against the built IServiceProvider ---

    [Test]
    public async Task AddDuckLanceVectorStore_OptionsProviderOverload_InvokedLazilyWithServiceProvider() {
        var services = new ServiceCollection();
        services.AddSingleton("vs_from_provider");

        var invocationCount = 0;

        services.AddDuckLanceVectorStore(sp => {
            invocationCount++;
            // Resolving a service registered directly on the ServiceCollection proves this callback really was
            // handed a capable IServiceProvider over the built container, not some placeholder.
            return NewOptions(databaseName: sp.GetRequiredService<string>());
        });

        // Registration alone must not invoke the provider callback: it only runs once something actually
        // resolves the store from the built IServiceProvider.
        await Assert.That(invocationCount).IsEqualTo(0);

        using var provider = services.BuildServiceProvider();
        using var store    = provider.GetRequiredService<DuckDBVectorStore>();

        await Assert.That(invocationCount).IsEqualTo(1);
        // The database name resolved through the provider-supplied IServiceProvider made it all the way into the
        // constructed store, confirming both "invoked" and "against the right provider" in one observable check.
        await Assert.That(Path.GetFileName(store.Resolver.DatabasePath)).IsEqualTo("vs_from_provider");
    }

    // --- A DI-registered IEmbeddingGenerator flows into the store options when the caller's options omit one ---

    // The DuckLance model builder rejects a vector property whose CLR type (here, string) isn't natively storable
    // UNLESS a compatible embedding generator is available to convert it into one of the supported vector types
    // -- see DuckDBModelBuilder, invoked synchronously and eagerly (no DB connection involved) from
    // VectorStore.GetCollection. That makes "does GetCollection succeed for a model that requires generation" an
    // honest, purely-DI observation point for whether DuckLanceServiceCollectionExtensions' options-fallback logic
    // actually threaded a generator through into the constructed DuckDBVectorStore. The three tests below use it
    // as exactly that: a positive case (DI fallback applies), a negative control (no generator anywhere -> the
    // same model fails, proving the positive case isn't succeeding for some unrelated reason), and a
    // non-substitution case (an options-level generator already present must win over the DI one).

    [Test]
    public async Task AddDuckLanceVectorStore_NoGeneratorOnOptions_FallsBackToDIRegisteredGenerator() {
        using var generator = new NeverInvokedGenerator<string>();

        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator>(generator);
        services.AddDuckLanceVectorStore(NewOptions()); // No EmbeddingGenerator set on these options.

        using var provider = services.BuildServiceProvider();
        using var store    = provider.GetRequiredService<DuckDBVectorStore>();

        // Throws InvalidOperationException ("... no embedding generator is configured ...") unless the
        // DI-registered generator flowed into the store's options.
        using var collection = store.GetCollection<string, GeneratedVectorRecord>("topics");

        await Assert.That(collection).IsNotNull();
    }

    [Test]
    public async Task AddDuckLanceVectorStore_NoGeneratorAnywhere_GetCollectionForGeneratedModel_Throws() {
        var services = new ServiceCollection();
        services.AddDuckLanceVectorStore(NewOptions());

        using var provider = services.BuildServiceProvider();
        using var store    = provider.GetRequiredService<DuckDBVectorStore>();

        await Assert
            .That(() => { store.GetCollection<string, GeneratedVectorRecord>("topics"); })
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AddDuckLanceVectorStore_GeneratorAlreadyOnOptions_IsNotReplacedByDIRegisteredGenerator() {
        using var optionsGenerator = new NeverInvokedGenerator<string>(); // Compatible with GeneratedVectorRecord.Text.
        using var diGenerator      = new NeverInvokedGenerator<int>();    // Incompatible with a string-sourced vector property.

        var options = NewOptions();
        options.EmbeddingGenerator = optionsGenerator;

        var services = new ServiceCollection();
        // Registered in DI, but incompatible: if the fallback wrongly replaced the options-level generator with
        // this one, GetCollection below would throw.
        services.AddSingleton<IEmbeddingGenerator>(diGenerator);
        services.AddDuckLanceVectorStore(options);

        using var provider = services.BuildServiceProvider();
        using var store    = provider.GetRequiredService<DuckDBVectorStore>();

        using var collection = store.GetCollection<string, GeneratedVectorRecord>("topics");

        await Assert.That(collection).IsNotNull();
    }

    static DuckDBVectorStoreOptions NewOptions(string databaseName = "duck.db") => new() { DatabasePath = Path.Combine(Path.GetTempPath(), "ducklance-di-tests", databaseName) };

    /// <summary>
    /// A minimal <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> that is never actually invoked: these tests
    /// only observe DI wiring at construction/model-build time (model building checks generator type
    /// compatibility, it never generates a real embedding), never real embedding generation.
    /// </summary>
    sealed class NeverInvokedGenerator<TInput> : IEmbeddingGenerator<TInput, Embedding<float>> {
        public void Dispose() { }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<TInput> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
        ) =>
            throw new NotImplementedException("Never invoked: these tests only observe DI wiring, not real embedding generation.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    sealed class GeneratedVectorRecord {
        [VectorStoreKey] public string Id { get; set; } = "";

        [VectorStoreData] public string Content { get; set; } = "";

        [VectorStoreVector(4)] public string Text { get; set; } = "";
    }
}