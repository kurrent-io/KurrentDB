// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using KurrentDB.Core;
using KurrentDB.Kontext.Embeddings;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KurrentDB.Kontext.Workspaces.Registry;

namespace KurrentDB.Kontext.Workspaces.Runtime;

public sealed class WorkspaceLifecycleManager(
	KontextStorageConfig storage,
	KontextEmbeddingsConfig embeddingsConfig,
	ISystemClient client,
	StoreRegistry<IFtsStore> ftsStores,
	StoreRegistry<IVectorStore> vectorStores,
	VectorIndexMetadataSource vectorMeta,
	NounPhraseExtractor npe,
	EmbeddingCache embeddingCache,
	Meter meter,
	Func<long> writerCheckpoint,
	ILoggerFactory loggerFactory)
	: IHostedService {

	readonly ConcurrentDictionary<string, WorkspaceRunner> _runners = new(StringComparer.Ordinal);
	readonly string _workspacesRoot = Path.Combine(storage.DataPath, "workspaces");
	readonly ILogger<WorkspaceLifecycleManager> _logger = loggerFactory.CreateLogger<WorkspaceLifecycleManager>();

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		foreach (var runner in _runners.Values)
			await runner.DisposeAsync().ConfigureAwait(false);
		_runners.Clear();
	}

	public void Mount(WorkspaceEntry workspace) =>
		GetOrAddRunner(workspace).Mount();

	public void Start(WorkspaceEntry workspace) {
		var runner = GetOrAddRunner(workspace);
		try {
			runner.Start();
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to start workspace '{Workspace}'", workspace.Name);
			throw;
		}
	}

	WorkspaceRunner GetOrAddRunner(WorkspaceEntry workspace) =>
		_runners.GetOrAdd(workspace.Name, _ => new WorkspaceRunner(
			workspace, Path.Combine(_workspacesRoot, workspace.Name),
			embeddingsConfig,
			client, ftsStores, vectorStores, vectorMeta.Value,
			npe, embeddingCache, meter, writerCheckpoint, loggerFactory));

	public async Task Stop(string name) {
		if (!_runners.TryGetValue(name, out var runner))
			throw new InvalidOperationException($"Workspace '{name}' is not running.");
		await runner.StopAsync().ConfigureAwait(false);
	}

	public async Task Remove(string name) {
		if (_runners.TryRemove(name, out var runner))
			await runner.DisposeAsync().ConfigureAwait(false);

		var dir = Path.Combine(_workspacesRoot, name);
		try {
			if (Directory.Exists(dir)) {
				Directory.Delete(dir, recursive: true);
				_logger.LogInformation("Workspace '{Workspace}' deleted", name);
			}
		} catch (Exception ex) {
			_logger.LogWarning(ex, "Failed to remove workspace directory '{Dir}'", dir);
		}
	}
}
