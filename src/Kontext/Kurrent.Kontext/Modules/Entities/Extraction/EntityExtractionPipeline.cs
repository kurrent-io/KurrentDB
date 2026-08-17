// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Pipelines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// The multi-stage extraction pipeline: a sequential cascade over the stages, folded by the
/// configured <see cref="IEntityMerger"/>, filtering invalid surface forms once at the end — one
/// choke point instead of per-extractor policing. Implements <see cref="IEntityExtractor"/>
/// itself, so a pipeline nests as a stage of another pipeline for free.
/// <para>Stages run sequentially on purpose: extraction happens on the projector's catch-up
/// path where memory pressure beats latency, and later stages may be skipped entirely
/// (<see cref="FirstSuccessMerger"/>, <see cref="EntityExtractionOptions.StopOnSuccess"/>).</para>
/// </summary>
public sealed class EntityExtractionPipeline : IEntityExtractor {
	readonly IStep<string, ExtractionResult> _cascade;

	EntityExtractionPipeline(IReadOnlyList<IEntityExtractor> stages, IStep<string, ExtractionResult> cascade) {
		StageNames = [.. stages.Select(stage => stage.Name)];
		_cascade   = cascade;
	}

	public string Name => "pipeline";

	public IReadOnlyList<string> StageNames { get; }

	/// <summary>Composes the cascade from pre-built options — the config-binding door.</summary>
	public static EntityExtractionPipeline From(IReadOnlyList<IEntityExtractor> stages, EntityExtractionOptions options) {
		var merger = options.Merger;
		var logger = options.Logger;

		var stop = options.StopOnSuccess || merger.StopsOnSuccess
			? (Func<ExtractionResult, bool>)(result => result.Entities.Count >= options.MinEntitiesForSuccess)
			: static _ => false;

		var cascade = Steps.Cascade<string, ExtractionResult, ExtractionResult>(
			[.. stages.Select(AsStep)],
			stop,
			(results, _) => Merge(merger, results).FilterInvalid(),
			options.FallbackOnError
				? (ex, index) => {
					// A broken stage must not take extraction down with it — the remaining stages
					// still produce a usable (if thinner) result.
					logger.LogWarning(ex, "Extraction stage {Stage} failed; continuing with the remaining stages", stages[index].Name);
					return true;
				}
				: null);

		return new(stages, cascade);
	}

	/// <summary>Composes the cascade over default options, tuned via <paramref name="configure"/> when given.</summary>
	public static EntityExtractionPipeline From(IReadOnlyList<IEntityExtractor> stages, Action<EntityExtractionOptions>? configure = null) {
		var options = new EntityExtractionOptions();
		configure?.Invoke(options);

		return From(stages, options);
	}

	public async ValueTask<ExtractionResult> ExtractAsync(string text, CancellationToken ct = default) {
		if (string.IsNullOrWhiteSpace(text))
			return ExtractionResult.Empty;

		return await _cascade.Execute(text, ct).ConfigureAwait(false);
	}

	// A single result stands as-is: the merge strategies only arbitrate between stages.
	static ExtractionResult Merge(IEntityMerger merger, IReadOnlyList<ExtractionResult> results) =>
		results.Count switch {
			0 => ExtractionResult.Empty,
			1 => results[0],
			_ => merger.Merge(results),
		};

	static IStep<string, ExtractionResult> AsStep(IEntityExtractor stage) =>
		Steps.Lambda<string, ExtractionResult>((text, ct) => stage.ExtractAsync(text, ct));
}

/// <summary>The pipeline's knobs — the defaults are the projector's shipped behavior.</summary>
public sealed class EntityExtractionOptions {
	/// <summary>How stage results fold into one (default <see cref="UnionMerger"/>).</summary>
	public IEntityMerger Merger { get; set; } = new UnionMerger();

	/// <summary>
	/// Stops after the first stage that yields at least <see cref="MinEntitiesForSuccess"/>
	/// entities — cheap stages first, expensive stages only as fallback.
	/// </summary>
	public bool StopOnSuccess { get; set; }

	public int MinEntitiesForSuccess { get; set; } = 1;

	/// <summary>
	/// Skips a failing stage instead of rethrowing (the default): a broken stage thins the
	/// result, it does not stall the projector.
	/// </summary>
	public bool FallbackOnError { get; set; } = true;

	public ILogger Logger { get; set; } = NullLogger.Instance;
}
