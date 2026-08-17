// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Modules.Entities.Resolution;
using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Consumers.Configuration;
using KurrentDB.Core.Hosting;
using KurrentDB.Core.Hosting.Experimental;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>
/// Builds the extraction pipeline the entity projector runs. A delegate rather than a
/// registered pipeline instance because the default stages load models asynchronously
/// (Catalyst's WikiNER), which a singleton registration cannot await.
/// </summary>
public delegate Task<EntityExtractionPipeline> EntityExtractionPipelineFactory(IServiceProvider services, CancellationToken ct);

/// <summary>
/// The entities read-model projector: consumes the SAME <c>$kontext/memories</c> stream as the
/// memory projector through its own Surge consumer and checkpoint, extracts entities from every
/// retained memory, resolves and deduplicates them against the entities read model, and applies
/// each batch through <see cref="KontextEntityWriter"/> before storing the checkpoint.
///
/// A separate projector on purpose: entities lag memories independently, replay independently
/// (a rebuild re-derives every entity from memory history), and extraction cost never sits in
/// the memory read model's path. The schema is the bootstrap's (<see cref="KontextEntitySchemaTask"/>);
/// the projector only ever writes tables the migration stream already created.
///
/// Owns the write surface, and lends it: the loop binds its connection to
/// <see cref="EntityWriteGate"/> and takes every batch as a turn on it, which is how offline
/// resolution (<see cref="KontextEntityResolutionService"/>) writes these tables without ever
/// racing a batch apply. Nothing else may write them.
///
/// Supervision restarts a dead loop with exponential backoff, the memory projector's rule: a
/// restart re-opens the connection, re-binds the gate, and resumes from the checkpoint — the
/// batch transaction makes the replay exact.
/// </summary>
public sealed class KontextEntityProjectorService(IServiceProvider services, NodeReadyWhen readyWhen = NodeReadyWhen.Operational)
	: SystemReadyBackgroundService(services, readyWhen, "KontextEntityProjector") {
	// Changing the key orphans the stored checkpoint and replays the read model from the start.
	const string CheckpointKey = "KontextEntityProjection";

	const int BatchSize = 500;

	static readonly TimeSpan BatchWindow         = TimeSpan.FromSeconds(5);
	static readonly TimeSpan InitialRestartDelay = TimeSpan.FromSeconds(5);
	static readonly TimeSpan MaximumRestartDelay = TimeSpan.FromSeconds(60);

	protected override async Task RunAsync(NodeSystemInfo nodeInfo, CancellationToken stoppingToken) {
		var log          = Services.GetRequiredService<ILoggerFactory>().CreateLogger<KontextEntityProjectorService>();
		var restartDelay = InitialRestartDelay;

		while (!stoppingToken.IsCancellationRequested) {
			try {
				await ProjectUntilStopped(stoppingToken).ConfigureAwait(false);
				break;
			} catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
				break;
			} catch (Exception ex) {
				log.LogError(ex, "Entity projector failed; restarting in {Delay}", restartDelay);

				try {
					await Task.Delay(restartDelay, stoppingToken).ConfigureAwait(false);
				} catch (OperationCanceledException) {
					break;
				}

				restartDelay = TimeSpan.FromTicks(Math.Min(restartDelay.Ticks * 2, MaximumRestartDelay.Ticks));
			}
		}
	}

	async Task ProjectUntilStopped(CancellationToken stoppingToken) {
		var dataSource      = Services.GetRequiredService<KontextDataSource>();
		var embeddings      = Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
		var consumerBuilder = Services.GetRequiredService<IConsumerBuilder>();
		var loggerFactory   = Services.GetRequiredService<ILoggerFactory>();
		var pipelineFactory = Services.GetRequiredService<EntityExtractionPipelineFactory>();
		var dedupOptions    = Services.GetService<EntityDeduplicationOptions>() ?? new();
		var writeGate       = Services.GetRequiredService<EntityWriteGate>();

		var pipeline = await pipelineFactory(Services, stoppingToken).ConfigureAwait(false);

		// The projector owns the write side end to end: the dedicated lance-redirected connection
		// (writers never rent), the checkpoint store — whose unqualified table lands in the lance
		// catalog via the redirection — and the per-batch transaction that carries the writes and
		// the checkpoint. The projection's resolution reads run on this same connection — the
		// only surface guaranteed to see the batches already applied.
		await using var connection = dataSource.OpenLanceWriter();

		var checkpoints = new KontextCheckpointStore(CheckpointKey);
		checkpoints.EnsureSchema(connection);

		// The dimension is the schema's — the FLOAT[N] column type and the writer's cast must
		// agree, and both come from KontextSchemaTask.Dimension.
		var projection = new KontextEntityProjection(
			pipeline, embeddings,
			new EmbeddingGenerationOptions { Dimensions = KontextSchemaTask.Dimension },
			new KontextEntityStore(connection),
			dedupOptions);

		var writer = new KontextEntityWriter(connection, KontextSchemaTask.Dimension);

		// Earliest when no checkpoint exists: the read model is rebuildable, so a fresh node
		// derives entities from the full memory history before serving anything.
		var startPosition = checkpoints.Load(connection);

		// Bound only for the loop's lifetime: outside it there IS no write surface, and resolution
		// asking for one gets told so rather than reaching a connection about to be disposed.
		using var writeBinding = writeGate.Bind(connection);

		await using var consumer = consumerBuilder
			.ConsumerId(CheckpointKey)
			.Filter(KontextConventions.Filters.MemoriesFilter)
			.InitialPosition(SubscriptionInitialPosition.Earliest)
			.StartPosition(startPosition)
			.DisableAutoCommit()
			.DisableResiliencePipeline()
			.LoggerFactory(loggerFactory)
			.Create();

		await foreach (var batch in consumer.Records(stoppingToken).ReadBatches(BatchSize, BatchWindow, stoppingToken).ConfigureAwait(false)) {
			// ONE turn for the whole batch, projection included: the projection's resolution reads
			// run on this same connection, so a resolution merge landing between those reads and
			// the apply they feed would decide against a store that no longer exists.
			await writeGate.RunAsync(
				async (writeConnection, ct) => {
					// The projection computes, the writer persists, the checkpoint claims — one
					// transaction: the native side commits atomically with the tx, lance commits per
					// statement — a crash leaves the position lagging and the batch replays, which the
					// writer is built to absorb (deterministic ids, MERGEs).
					var delta = await projection.ProjectAsync(batch, ct).ConfigureAwait(false);

					using var tx = writeConnection.BeginTransaction();

					writer.Apply(delta);

					checkpoints.Store(writeConnection, batch[^1].Position);

					tx.CommitOnDispose();
				}, stoppingToken).ConfigureAwait(false);
		}
	}
}

public static class KontextEntityProjectorWireUpExtensions {
	extension(IServiceCollection services) {
		/// <summary>
		/// Registers the entity projector and its composition seams. <paramref name="pipeline"/>
		/// replaces the default extraction pipeline (Catalyst WikiNER + pattern extraction,
		/// union-merged); <paramref name="deduplication"/> tunes the merge/flag thresholds. The
		/// write gate and the resolution service land here too — resolution is only reachable
		/// through the projector's connection, so the two register together or not at all.
		/// </summary>
		public IServiceCollection AddKontextEntityProjector(
			EntityExtractionPipelineFactory? pipeline = null,
			Action<EntityDeduplicationOptions>? deduplication = null
		) {
			services.AddSystemReadiness();

			services.TryAddSingleton(sp => new KontextEntityStore(sp.GetRequiredService<KontextDataSource>()));

			services.TryAddSingleton<EntityWriteGate>();
			services.TryAddSingleton<KontextEntityResolutionService>();

			if (deduplication is not null) {
				var options = new EntityDeduplicationOptions();
				deduplication(options);
				services.TryAddSingleton(options);
			}

			services.TryAddSingleton(pipeline ?? DefaultPipeline);

			services.AddHostedService(sp => new KontextEntityProjectorService(sp));
			return services;
		}
	}

	static async Task<EntityExtractionPipeline> DefaultPipeline(IServiceProvider services, CancellationToken ct) =>
		EntityExtractionPipeline.From(
			stages: [
				await CatalystEntityExtractor.CreateAsync().ConfigureAwait(false),
				new PatternEntityExtractor()
			],
			configure: options => {
				options.Logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<EntityExtractionPipeline>();
			});
}
