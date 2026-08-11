// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext;

/// <summary>
/// Extension methods for registering the retrieval pipeline with dependency injection. Lives in
/// the host, not <c>Kurrent.Kontext.Retrieval</c>, because it binds the host-owned
/// <see cref="KontextDataStore"/> as the pipeline's <see cref="IMemoryIndex"/>.
/// </summary>
[PublicAPI]
public static class KontextRetrievalServiceCollectionExtensions {
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services) {
        public IServiceCollection AddKontextRetrieval() {
            // First-wins: a pre-registered IKontextRetriever (composed directly with
            // KontextRetriever.New()) beats the default Hybrid chain.
            services.TryAddSingleton<IKontextRetriever>(CreateDefaultRetriever);

            return services;
        }
    }

    static IKontextRetriever CreateDefaultRetriever(IServiceProvider sp) =>
        KontextRetriever.New()
            .Hybrid(
                sp.GetRequiredService<KontextDataStore>(),
                sp.GetRequiredService<EmbeddingGenerator>(),
                ResolveServices(sp.GetService<KontextRetrievalOptions>() ?? new KontextRetrievalOptions(), sp))
            .Build();

    /// <summary>
    /// Applies service-provider-resolved services to the options as overrides. Each service only
    /// overrides the corresponding option if it is registered.
    /// </summary>
    static KontextRetrievalOptions ResolveServices(KontextRetrievalOptions options, IServiceProvider sp) {
        if (sp.GetService<TimeProvider>() is { } time)
            options.Time = time;

        return options;
    }
}
