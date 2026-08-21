// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Contracts.V3.Memory;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;

using Candidate = (string MemoryId, System.Collections.Generic.IReadOnlyList<Kurrent.Kontext.Modules.Entities.Extraction.ExtractedEntity> Entities);

namespace Kurrent.Kontext.Modules.Entities;

using Contracts = Kurrent.Kontext.Contracts.V3.Entities;

/// <summary>
/// Reads each new memory and has the resolver decide every name's fate. Each decision lands as
/// an event where the mention links to its entity or carries the one it created.
/// </summary>
public sealed class KontextEntityResolution : ProcessingModule {
    public KontextEntityResolution(KontextEntityResolver resolver, IEntityExtractor extractor, IProducerBuilder producerBuilder) {
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
                    Mentions   = { result.Entities.Select(entity => Mention(entity, resolutions[EntityKey.For(entity.EntityType, entity.Text)])) },
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
        });
    }

    static Contracts.EntityMention Mention(ExtractedEntity span, ResolvedEntity resolved) {
        var mention = new Contracts.EntityMention {
            SpanText   = span.Text,
            Confidence = resolved.Confidence,
            ResolvedBy = resolved.Method,
        };

        if (resolved.Method is Contracts.ResolutionMethod.Created)
            mention.Created = new Contracts.Entity {
                EntityId      = resolved.EntityId,
                Type          = span.EntityType,
                CanonicalName = span.Text,
                Aliases       = { span.Text },
            };
        else
            mention.EntityId = resolved.EntityId;

        return mention;
    }
}
