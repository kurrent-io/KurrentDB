// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using KurrentDB.Core;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Kontext.Diagnostics;
using KurrentDB.Kontext.Embeddings;
using KurrentDB.Kontext.Indexing;
using KurrentDB.Kontext.Indexing.USearch;
using KurrentDB.Kontext.Search;
using KurrentDB.Kontext.Workspaces.Registry;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Kontext.Workspaces.Runtime;

public sealed class WorkspaceRunner(
	WorkspaceEntry workspace,
	string workspaceDir,
	KontextEmbeddingsConfig embeddingsConfig,
	ISystemClient client,
	StoreRegistry<IFtsStore> ftsRegistry,
	StoreRegistry<IVectorStore> vectorRegistry,
	VectorIndexMetadata vectorMeta,
	NounPhraseExtractor npe,
	EmbeddingCache embeddingCache,
	Meter meter,
	Func<long> writerCheckpoint,
	ILoggerFactory loggerFactory) : IAsyncDisposable {
	static readonly IndexKind[] Kinds = [IndexKind.Events, IndexKind.Memory, IndexKind.EventsStreams, IndexKind.MemoryStreams];

	readonly ILogger<WorkspaceRunner> _logger = loggerFactory.CreateLogger<WorkspaceRunner>();
	readonly KontextProgressTracker _tracker = new(
		workspace.Name, workspace.IndexingStatus, meter, writerCheckpoint);

	CancellationTokenSource? _cts;
	IFtsStore[]? _ftsStores;
	IVectorStore[]? _vectorStores;
	Task? _ftsLoop;
	Task? _embeddingLoop;
	WorkspaceBloomFilters? _bloomFilters;

	bool _disposed;

	public WorkspaceEntry Workspace => workspace;

	public void Mount() {
		ObjectDisposedException.ThrowIf(_disposed, this);
		Directory.CreateDirectory(workspaceDir);

		// the vector stores and bloom filters are mounted so that they are readable by
		// the cross-workspace embedding cache even if the workspace is stopped
		MountVectorStores();
		MountBloomFilters();
	}

	public void Start() {
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_cts != null)
			return;

		Directory.CreateDirectory(workspaceDir);

		MountVectorStores();
		MountBloomFilters();
		MountFtsStores();

		_cts = new CancellationTokenSource();
		var token = _cts.Token;
		var filter = BuildFilter(workspace);

		if (workspace.FullTextIndexingEnabled)
			_ftsLoop = Task.Run(() => CreateFtsSubscription(filter).RunAsync(token), token);

		if (workspace.SemanticIndexingEnabled)
			_embeddingLoop = Task.Run(() => CreateEmbeddingSubscription(filter).RunAsync(token), token);

		_logger.LogInformation("Workspace '{Workspace}' started", workspace.Name);
	}

	void MountVectorStores() {
		if (!workspace.SemanticIndexingEnabled || _vectorStores != null)
			return;

		var embDir = Path.Combine(workspaceDir, "embeddings");
		Directory.CreateDirectory(embDir);
		_vectorStores = Kinds.Select(k => (IVectorStore)new USearchVectorStore(
			embDir, k.PathSegment(), vectorMeta, loggerFactory.CreateLogger<USearchVectorStore>())).ToArray();
		Mount(_vectorStores, vectorRegistry);
	}

	void MountBloomFilters() {
		if (!workspace.SemanticIndexingEnabled || _bloomFilters != null)
			return;

		_bloomFilters = new WorkspaceBloomFilters(workspaceDir, loggerFactory.CreateLogger<WorkspaceBloomFilters>());
		embeddingCache.MountBloomFilters(workspace.Name, _bloomFilters);
	}

	void MountFtsStores() {
		if (!workspace.FullTextIndexingEnabled || _ftsStores != null)
			return;

		var ftsDir = Path.Combine(workspaceDir, "fts");
		Directory.CreateDirectory(ftsDir);
		_ftsStores = Kinds.Select(k => (IFtsStore)new LuceneFtsStore(
			ftsDir, k.PathSegment(), loggerFactory.CreateLogger<LuceneFtsStore>())).ToArray();
		Mount(_ftsStores, ftsRegistry);
	}

	void UnmountVectorStores() {
		if (_vectorStores != null) {
			Unmount(_vectorStores, vectorRegistry);
			_vectorStores = null;
		}
	}

	void UnmountBloomFilters() {
		if (_bloomFilters != null) {
			embeddingCache.UnmountBloomFilters(workspace.Name);
			_bloomFilters.Dispose();
			_bloomFilters = null;
		}
	}

	void UnmountFtsStores() {
		if (_ftsStores != null) {
			Unmount(_ftsStores, ftsRegistry);
			_ftsStores = null;
		}
	}

	public async Task StopAsync() {
		if (_cts == null)
			return;

		await _cts.CancelAsync();

		try { await Task.WhenAll(_ftsLoop ?? Task.CompletedTask, _embeddingLoop ?? Task.CompletedTask).ConfigureAwait(false); } catch (OperationCanceledException) { /* expected */ }

		_cts.Dispose();
		_cts = null;
		_ftsLoop = null;
		_embeddingLoop = null;

		// The vector stores and bloom filters stay mounted for the cross-workspace embedding cache.
		UnmountFtsStores();

		_logger.LogInformation("Workspace '{Workspace}' stopped", workspace.Name);
	}

	public async ValueTask DisposeAsync() {
		if (_disposed)
			return;
		_disposed = true;

		await StopAsync().ConfigureAwait(false);

		UnmountFtsStores();
		UnmountVectorStores();
		UnmountBloomFilters();
	}

	FtsSubscription CreateFtsSubscription(IEventFilter filter) =>
		new(client, workspace, filter,
			Path.Combine(workspaceDir, "checkpoint.fts"),
			_ftsStores![0], _ftsStores[1], _ftsStores[2], _ftsStores[3],
			npe, workspace.IndexingStatus, _tracker,
			loggerFactory.CreateLogger<FtsSubscription>());

	EmbeddingSubscription CreateEmbeddingSubscription(IEventFilter filter) =>
		new(client, workspace, filter,
			Path.Combine(workspaceDir, "checkpoint.embeddings"),
			_vectorStores![0], _vectorStores[1], _vectorStores[2], _vectorStores[3],
			embeddingCache, _bloomFilters!, embeddingsConfig, workspace.IndexingStatus, _tracker,
			loggerFactory.CreateLogger<EmbeddingSubscription>());

	void Mount<TStore>(TStore[] stores, StoreRegistry<TStore> registry) where TStore : class {
		for (var i = 0; i < Kinds.Length; i++)
			registry.TryAdd(new WorkspaceIndex(workspace.Name, Kinds[i]), stores[i]);
	}

	void Unmount<TStore>(TStore[] stores, StoreRegistry<TStore> registry) where TStore : class, IDisposable {
		for (var i = 0; i < Kinds.Length; i++) {
			registry.TryRemove(new WorkspaceIndex(workspace.Name, Kinds[i]), out _);
			stores[i].Dispose();
		}
	}

	static IEventFilter BuildFilter(WorkspaceEntry workspace) {
		if (WorkspaceNaming.IsDefault(workspace.Name))
			return EventFilter.Unfiltered;

		var prefixes = new string[workspace.FilterRules.Count + 1];
		prefixes[0] = workspace.MemoryStreamPrefix;
		for (var i = 0; i < workspace.FilterRules.Count; i++)
			prefixes[i + 1] = workspace.FilterRules[i].StreamPrefix;
		return EventFilter.StreamName.Prefixes(false, prefixes);
	}
}
