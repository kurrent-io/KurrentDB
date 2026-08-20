// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.WellKnownTypes;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Contracts.V3.Memory;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Surge.Processors;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Producers.Configuration;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>
/// Reads each new memory, spots the names in it, links each name to a known entity or creates
/// one, and writes the result as an event.
/// </summary>
public sealed class KontextEntityResolution : ProcessingModule {
    public KontextEntityResolution(KontextDataSource dataSource, IEntityExtractor extractor, IProducerBuilder producerBuilder) {
        Resolver  = new KontextEntityResolver(dataSource);
        Extractor = extractor;
        Producer  = producerBuilder.ProducerId("KontextEntityResolutionProducer").Create();

        Process<MemoriesRetained>(async (retained, ctx) => {
            try {
                var events = await ResolveRetained(retained, ctx.CancellationToken);

                if (events.Count == 0)
                    return;

                await Produce(events);
            }
            catch (Exception ex) {
                throw new Exception($"Failed to resolve entities on {nameof(MemoriesRetained)}", ex);
            }
        });
    }

    KontextEntityResolver Resolver  { get; }
    IEntityExtractor      Extractor { get; }
    IProducer             Producer  { get; }

    Dictionary<string, string> CreatedIds { get; } = [];

    async ValueTask<List<EntitiesMentioned>> ResolveRetained(MemoriesRetained retained, CancellationToken ct) {
        var extractions = new List<(string MemoryId, IReadOnlyList<ExtractedEntity> Entities)>(retained.Memories.Count);

        foreach (var entry in retained.Memories)
            extractions.Add((entry.MemoryId, await Extractor.ExtractAsync(entry.Memory.Content, ct)));

        var unknown = extractions
            .SelectMany(extraction => extraction.Entities)
            .Select(entity => EntityId.Normalize(entity.Text))
            .Where(normalized => !CreatedIds.ContainsKey(normalized))
            .ToHashSet();

        var known = await Resolver.ResolveExactAsync(unknown, ct);

        var resolvedAt = Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow());
        var events     = new List<EntitiesMentioned>();

        foreach (var (memoryId, entities) in extractions) {
            if (entities.Count == 0)
                continue;

            var evt = new EntitiesMentioned { MemoryId = memoryId, ResolvedAt = resolvedAt };

            foreach (var entity in entities)
                evt.Mentions.Add(Resolve(entity, known));

            events.Add(evt);
        }

        return events;
    }

    EntityMention Resolve(ExtractedEntity extracted, IReadOnlyDictionary<string, string> known) {
        var normalized = EntityId.Normalize(extracted.Text);

        if (CreatedIds.TryGetValue(normalized, out var entityId) || known.TryGetValue(normalized, out entityId!)) {
            return new EntityMention {
                SpanText   = extracted.Text,
                EntityId   = entityId,
                Confidence = 1.0,
                ResolvedBy = ResolutionMethod.Exact,
            };
        }

        entityId = EntityId.For(extracted.EntityType, extracted.Text);

        CreatedIds[normalized] = entityId;

        return new EntityMention {
            SpanText = extracted.Text,
            Created = new Entity {
                EntityId      = entityId,
                Type          = extracted.EntityType,
                CanonicalName = extracted.Text,
                Aliases       = { extracted.Text },
            },
            Confidence = 1.0,
            ResolvedBy = ResolutionMethod.Created,
        };
    }

    async ValueTask Produce(List<EntitiesMentioned> events) {
        var request = events
            .Aggregate(
                ProduceRequest.Builder.Stream(KontextConventions.Streams.EntitiesStreamPrefix),
                (builder, evt) => builder.Message(evt))
            .Create();

        await Producer.Produce(request, throwOnError: true);
    }
}
