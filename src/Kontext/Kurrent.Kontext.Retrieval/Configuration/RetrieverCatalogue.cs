// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Frozen;
using static Kurrent.Kontext.Retrieval.RetrieverCatalogueItem;
using EmbeddingGenerator = Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>;

namespace Kurrent.Kontext.Retrieval;

/// <summary>The services a retrieval chain composes over: the read model, the query embedder, and the knobs.</summary>
public sealed record RetrieverParts(IMemoryIndex Index, EmbeddingGenerator Embeddings, KontextRetrievalOptions Options) {
	/// <summary>
	/// The entities read model, when the host has one. Optional and NOT positional on purpose: a
	/// chain that never had it must keep composing exactly as it did, and absent must mean the
	/// entity stage falls through rather than guessing at a signal.
	/// </summary>
	public IEntityIndex? Entities { get; init; }

	/// <summary>Parts over default options, tuned via <paramref name="configure"/> when given.</summary>
	public static RetrieverParts Of(IMemoryIndex index, EmbeddingGenerator embeddings, Action<KontextRetrievalOptions>? configure = null) {
		var options = new KontextRetrievalOptions();
		configure?.Invoke(options);

		return new(index, embeddings, options);
	}
}

/// <summary>
/// The catalogue of shipped retrieval chains, for the consumers that select a variant at
/// RUNTIME: the host's <c>AddKontextRetrieval</c> (config-driven) and benchmark sweeps that
/// enumerate every variant. The definitions live on <see cref="KontextRetriever"/>'s static
/// factories — call those directly when the variant is known at compile time. A host with a
/// custom chain registers its own <see cref="IKontextRetriever"/> (registration is first-wins).
/// </summary>
public class RetrieverCatalogue {
	public static readonly RetrieverCatalogue Instance = new();

	FrozenDictionary<string, RetrieverCatalogueItem> Items { get; }

	RetrieverCatalogue() =>
		Items = new Dictionary<string, RetrieverCatalogueItem> {
			[RetrieverVariants.Focused] = For(RetrieverVariants.Focused, KontextRetriever.Focused),
			[RetrieverVariants.Default] = For(RetrieverVariants.Default, KontextRetriever.Default),
			[RetrieverVariants.Hybrid]  = For(RetrieverVariants.Hybrid, KontextRetriever.Hybrid),
			[RetrieverVariants.Legacy]  = For(RetrieverVariants.Legacy, KontextRetriever.Legacy),
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	public static bool TryGetRetriever(string variant, out RetrieverCatalogueItem item) =>
		Instance.Items.TryGetValue(variant, out item);

	public static IEnumerable<RetrieverCatalogueItem> GetRetrievers() => Instance.Items.Values;

	public static string[] VariantNames() => Instance.Items.Keys.ToArray();
}

[PublicAPI]
public readonly record struct RetrieverCatalogueItem() {
	public static readonly RetrieverCatalogueItem None = new();

	public string Variant { get; init; } = "";

	public Func<RetrieverParts, KontextRetriever> Factory { get; init; } =
		static _ => throw new InvalidOperationException("The item names no variant.");

	public KontextRetriever Create(RetrieverParts parts) => Factory(parts);

	public static RetrieverCatalogueItem For(string variant, Func<RetrieverParts, KontextRetriever> factory) =>
		new() { Variant = variant, Factory = factory };
}

/// <summary>The shipped variant names — hosts and benchmarks select by these.</summary>
public static class RetrieverVariants {
	public const string Focused = "focused";
	public const string Default = "default";
	public const string Hybrid  = "hybrid";
	public const string Legacy  = "legacy";
}
