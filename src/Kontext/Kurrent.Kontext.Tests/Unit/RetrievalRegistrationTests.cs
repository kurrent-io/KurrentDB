// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Kurrent.Kontext.Tests;

/// <summary>
/// The AddKontextRetrieval configure hook is the variant seam: it must own the whole composition (no
/// default searches or stages leak in) and it must lose to a pre-registered retriever, because
/// registration is first-wins.
/// </summary>
[Category("Retrieval")]
public class RetrievalRegistrationTests {
    [Test]
    public async ValueTask configure_hook_replaces_the_default_composition() {
        List<string> expectedIds = ["a", "b"];

        var services = new ServiceCollection().AddKontextRetrieval((pipeline, _) =>
            pipeline.AddSearch(new FixedSearch(
                new(Memory(expectedIds[0]), 0.9),
                new(Memory(expectedIds[1]), 0.8))));

        await using var provider = services.BuildServiceProvider();

        var retriever = provider.GetRequiredService<IKontextRetriever>();
        var result    = await retriever.RetrieveAsync(new() { Text = "query" });

        var ids = result.Select(scored => scored.Memory.MemoryId).ToList();
        await Assert.That(ids).IsEquivalentTo(expectedIds, CollectionOrdering.Matching);
    }

    [Test]
    public async ValueTask pre_registered_retriever_wins_over_add_kontext_retrieval() {
        var custom = KontextRetriever.New()
            .AddSearch(new FixedSearch(new SearchCandidate(Memory("mine"), 1.0)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IKontextRetriever>(custom);
        services.AddKontextRetrieval();

        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<IKontextRetriever>()).IsSameReferenceAs(custom);
    }

    static Contracts.StoredMemory Memory(string id) => new() {
        MemoryId       = id,
        Content        = "content",
        LastAccessedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch),
    };

    sealed class FixedSearch(params SearchCandidate[] candidates) : ISearch {
        public string Name => RetrievalSources.Vector;

        public ValueTask<CandidateSet> SearchAsync(PlannedQuery query, CancellationToken ct = default) =>
            ValueTask.FromResult(new CandidateSet(Name, candidates));
    }
}
