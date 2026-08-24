// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Contracts.V3.Memory;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Entities.Data;
using Kurrent.Kontext.Entities.Extraction;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;
using Microsoft.Extensions.AI;

using Candidate = (string MemoryId, System.Collections.Generic.IReadOnlyList<Kurrent.Kontext.Entities.Extraction.ExtractedEntity> Entities);

namespace Kurrent.Kontext.Entities;

using Contracts = Kurrent.Kontext.Contracts.V3.Entities;

/// <summary>
/// Reads each new memory and has the resolver decide every name's fate. Each decision lands as
/// an event where the mention links to its entity or carries the one it created — and then in the
/// catalog itself, so the next batch resolves against every entity this one created instead of
/// waiting on the projector.
/// </summary>
public sealed class KontextEntityResolution : ProcessingModule {
    public KontextEntityResolution(
        KontextEntityResolver resolver,
        IEntityExtractor extractor,
        IProducerBuilder producerBuilder,
        KontextDataSource dataSource,
        IEmbeddingGenerator<string, Embedding<float>> embeddings
    ) {
        var producer = producerBuilder.ProducerId("KontextEntityResolutionProducer").Create();

        Process<MemoriesRetained>(async (retained, ctx) => {
            var candidates = new List<Candidate>(retained.Memories.Count);

            foreach (var memory in retained.Memories)
                candidates.Add((memory.MemoryId, await extractor
                    .ExtractAsync(memory.Memory.Content, ctx.CancellationToken)
                    .ConfigureAwait(false)));

            var resolutions = await resolver
                .ResolveAsync(candidates.SelectMany(candidate => candidate.Entities), ctx.CancellationToken)
                .ConfigureAwait(false);

            var events = candidates
                .Where(result => result.Entities.Count > 0)
                .Select(result => new Contracts.EntitiesMentioned {
                    MemoryId   = result.MemoryId,
                    ResolvedAt = Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow()),
                    Mentions   = { result.Entities.Select(entity => entity.ToContract(resolutions[EntityKey.For(entity.EntityType, entity.Text)])) },
                })
                .ToList();

            if (events.Count == 0)
                return;

            var request = events
                .Aggregate(
                    ProduceRequest.Builder.Stream(KontextConventions.Streams.EntitiesStreamPrefix),
                    (builder, evt) => builder.Message(evt))
                .Create();

            await producer.Produce(request, throwOnError: true).ConfigureAwait(false);

            // Read-your-writes: the events are durable, so put them in the catalog now rather than
            // waiting on the projector — a crash here only costs the projector re-applying them.
            await using var connection = dataSource.OpenLanceWriter();

            var writer = new KontextEntityWriter(
                connection, embeddings,
                new EmbeddingGenerationOptions { Dimensions = KontextIndexConstants.VectorsDimension });

            using var tx = connection.BeginTransaction();

            await writer.ApplyAsync(events, ctx.CancellationToken).ConfigureAwait(false);

            tx.CommitOnDispose();
        });
    }
}
