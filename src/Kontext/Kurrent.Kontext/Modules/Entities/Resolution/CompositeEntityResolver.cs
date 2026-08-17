// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Pipelines;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// The chained resolver the dedup policy consumes: strategies cascade in the order given —
/// canonically exact, then fuzzy, then semantic — and the first match wins. Cheap certainty
/// first, expensive guesswork only when everything cheaper missed. Method precedence over raw
/// score on purpose: an exact hit in either pool must beat a fuzzy hit however it scored.
/// </summary>
public sealed class CompositeEntityResolver : IEntityResolver {
	readonly IStep<ResolutionProbe, EntityResolution> _cascade;

	public CompositeEntityResolver(params IReadOnlyList<IEntityResolver> resolvers) =>
		_cascade = Steps.Cascade<ResolutionProbe, EntityResolution, EntityResolution>(
			[.. resolvers.Select(AsStep)],
			static resolution => resolution.IsMatch,
			static (results, _) => results.Count > 0 && results[^1].IsMatch ? results[^1] : EntityResolution.Unmatched);

	/// <summary>The canonical chain over one store: exact → fuzzy → semantic, reference thresholds.</summary>
	public static CompositeEntityResolver Over(KontextEntityStore store) => new(
		new ExactEntityResolver(store),
		new FuzzyEntityResolver(store),
		new SemanticEntityResolver(store));

	public ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default) =>
		_cascade.Execute(probe, ct);

	static IStep<ResolutionProbe, EntityResolution> AsStep(IEntityResolver resolver) =>
		Steps.Lambda<ResolutionProbe, EntityResolution>((probe, ct) => resolver.ResolveAsync(probe, ct));
}
