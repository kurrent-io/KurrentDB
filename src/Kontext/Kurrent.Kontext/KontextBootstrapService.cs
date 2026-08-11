// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Embeddings;
using Kurrent.Kontext.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kurrent.Kontext;

/// <summary>
/// The startup gate, hosted FIRST so it completes before any writer starts: probes the
/// embedding generator's dimension against the configured one (a mismatch is a loud startup
/// failure, never a poisoned vector store — the probe also forces the model to load, so a
/// broken model surfaces here too), then creates the memories schema. The records indexer
/// bootstraps its own schema; the memories projector assumes this ran.
/// </summary>
public sealed class KontextBootstrapService(IServiceProvider services) : IHostedService {
    public async Task StartAsync(CancellationToken cancellationToken) {
        var options   = services.GetRequiredService<KontextOptions>();
        var generator = services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        await generator.EnsureDimensionAsync(options.Embeddings.Dimension, cancellationToken).ConfigureAwait(false);

        var schema = new KontextSchema(
            services.GetRequiredService<KontextConnectionPool>(),
            services.GetRequiredService<KontextSchemaOptions>());

        await schema.CreateAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
