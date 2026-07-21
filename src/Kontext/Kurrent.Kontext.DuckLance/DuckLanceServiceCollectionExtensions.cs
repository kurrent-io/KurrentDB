using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods to register <see cref="DuckDBVectorStore"/> instances as <see cref="VectorStore"/> on an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Only the store itself is registered by these extensions. <see cref="DuckDBCollection{TKey, TRecord}"/> has an internal
/// constructor and is never registered individually on the <see cref="IServiceCollection"/>; obtain a collection by resolving
/// the <see cref="DuckDBVectorStore"/> (or <see cref="VectorStore"/>) and calling <see cref="VectorStore.GetCollection{TKey, TRecord}"/>
/// or <see cref="VectorStore.GetDynamicCollection"/> on it.
/// </remarks>
public static class DuckLanceServiceCollectionExtensions {
    /// <summary>
    /// Registers a <see cref="DuckDBVectorStore"/> as <see cref="VectorStore"/>, with the specified options and service lifetime.
    /// </summary>
    /// <inheritdoc cref="AddKeyedDuckLanceVectorStore(IServiceCollection, object?, DuckDBVectorStoreOptions, ServiceLifetime)"/>
    public static IServiceCollection AddDuckLanceVectorStore(
        this IServiceCollection services,
        DuckDBVectorStoreOptions options,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    ) =>
        services.AddKeyedDuckLanceVectorStore(null, options, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="DuckDBVectorStore"/> as <see cref="VectorStore"/>, with the specified options and service lifetime.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="VectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="options">Options to configure the vector store.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddKeyedDuckLanceVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        DuckDBVectorStoreOptions options,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    ) =>
        services.AddKeyedDuckLanceVectorStore(serviceKey, optionsProvider: _ => options, lifetime);

    /// <summary>
    /// Registers a <see cref="DuckDBVectorStore"/> as <see cref="VectorStore"/>, with an options provider invoked lazily
    /// against the built <see cref="IServiceProvider"/>, and the specified service lifetime.
    /// </summary>
    /// <inheritdoc cref="AddKeyedDuckLanceVectorStore(IServiceCollection, object?, Func{IServiceProvider, DuckDBVectorStoreOptions}, ServiceLifetime)"/>
    public static IServiceCollection AddDuckLanceVectorStore(
        this IServiceCollection services,
        Func<IServiceProvider, DuckDBVectorStoreOptions> optionsProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    ) =>
        services.AddKeyedDuckLanceVectorStore(null, optionsProvider, lifetime);

    /// <summary>
    /// Registers a keyed <see cref="DuckDBVectorStore"/> as <see cref="VectorStore"/>, with an options provider invoked lazily
    /// against the built <see cref="IServiceProvider"/>, and the specified service lifetime.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to register the <see cref="VectorStore"/> on.</param>
    /// <param name="serviceKey">The key with which to associate the vector store.</param>
    /// <param name="optionsProvider">A callback, invoked against the built <see cref="IServiceProvider"/>, that produces the options to configure the vector store.</param>
    /// <param name="lifetime">The service lifetime for the store. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddKeyedDuckLanceVectorStore(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, DuckDBVectorStoreOptions> optionsProvider,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    ) {
        services.Add(
            new(
                typeof(DuckDBVectorStore), serviceKey, factory: (sp, _) => {
                    var options = GetStoreOptions(sp, optionsProvider);
                    return new DuckDBVectorStore(options);
                }, lifetime));

        services.Add(
            new(
                typeof(VectorStore), serviceKey,
                factory: static (sp, key) => sp.GetRequiredKeyedService<DuckDBVectorStore>(key), lifetime));

        return services;

        // Resolves options, falling back to the container's IEmbeddingGenerator when the options carry none.
        static DuckDBVectorStoreOptions GetStoreOptions(IServiceProvider sp, Func<IServiceProvider, DuckDBVectorStoreOptions> optionsProvider) {
            var options = optionsProvider(sp);

            if (options.EmbeddingGenerator is not null)
                return options;

            var embeddingGenerator = sp.GetService<IEmbeddingGenerator>();

            // The fallback lands on a fresh copy so the caller's original options instance is never mutated.
            return embeddingGenerator is null
                ? options
                : new(options) { EmbeddingGenerator = embeddingGenerator };
        }
    }
}