// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Kontext.Embeddings;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Search;
using Microsoft.Extensions.AI;
using KurrentDB.Kontext.Workspaces;
using KurrentDB.Kontext.Workspaces.ControlPlane;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext;

public static class KontextInitializer {
	public static async Task InitializeKontextAsync(this IServiceProvider services, CancellationToken ct = default) {
		var ready = services.GetRequiredService<KontextReadySignal>();
		try {
			services.GetRequiredService<KontextStorageConfig>().Validate();

			var localEmbedding = services.GetService<EmbeddingService>();
			if (localEmbedding != null)
				await localEmbedding.InitializeAsync();

			await services.GetRequiredService<NounPhraseExtractor>().InitializeAsync();

			var crossEncoder = services.GetService<CrossEncoderService>();
			if (crossEncoder != null)
				await crossEncoder.InitializeAsync();

			var storage = services.GetRequiredService<KontextStorageConfig>();
			services.GetRequiredService<VectorIndexMetadataSource>().Value =
				await VectorIndexMetadata.LoadOrProbeAsync(
					services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
					services.GetRequiredService<KontextEmbeddingsConfig>().Provider.ToString(),
					cachePath: Path.Combine(storage.DataPath, "embeddings.meta.cache"), ct);

			// Register the workspace-management event schemas before the command service serialises any
			await new RegisterWorkspaceEvents(services).Run(ct);

			// Auto-create the default workspace if it has never been created. Create is idempotent
			// for the default rules, so it's safe to run on every startup.
			var commandService = services.GetRequiredService<WorkspaceCommandService>();
			var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(KontextInitializer));
			await CreateDefaultWorkspaceAsync(commandService, logger, ct);

			ready.SetReady();
		} catch (Exception ex) {
			ready.SetFailed(ex);
			throw;
		}
	}

	static async Task CreateDefaultWorkspaceAsync(
		WorkspaceCommandService commandService, ILogger logger, CancellationToken ct) {
		var request = new CreateWorkspaceRequest(WorkspaceNaming.DefaultName, [new FilterRule("", null)]);
		const int retryDelaySec = 2;
		var waiting = false;

		while (true) {
			ct.ThrowIfCancellationRequested();
			try {
				var result = await commandService.Handle(request, ct);
				result.ThrowIfError();
				return;
			} catch (Exception ex) when (ex.GetBaseException() is
				ReadResponseException.NotHandled.ServerNotReady or
				ReadResponseException.NotHandled.ServerBusy or
				ReadResponseException.NotHandled.NoLeaderInfo or
				ReadResponseException.NotHandled.LeaderInfo) {
				if (!waiting) {
					logger.LogInformation(
						"Waiting for the node to become ready before creating the default workspace...");
					waiting = true;
				}
				await Task.Delay(TimeSpan.FromSeconds(retryDelaySec), ct);
			}
		}
	}
}
