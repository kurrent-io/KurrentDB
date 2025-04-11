// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.NET.Data;
using EventStore.Core.Bus;
using EventStore.Core.Duck.Default;
using EventStore.Core.Duck.Infrastructure;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.Chunks;
using Serilog;
using static EventStore.Core.Messages.SystemMessage;

namespace EventStore.Core.Duck;

class DuckDbIndexBuilder<TStreamId> : IAsyncHandle<SystemReady>, IAsyncHandle<BecomeShuttingDown> {
	static readonly ILogger Log = Serilog.Log.Logger.ForContext<DuckDbIndexBuilder<TStreamId>>();
	InternalSubscription _subscription;
	readonly IndexCheckpointStore<TStreamId> _checkpointStore;
	readonly DuckDb _db;
	readonly IPublisher _publisher;

	internal DefaultIndex<TStreamId> DefaultIndex { get; }

	[Experimental("DuckDBNET001")]
	public DuckDbIndexBuilder(TFChunkDbConfig dbConfig, IPublisher publisher, IReadIndex<TStreamId> index) {
		_publisher = publisher;
		_db = new(dbConfig);
		_db.InitDb();
		DefaultIndex = new(_db, index);
		new InlineFunctions<TStreamId>(_db, publisher).Run();
		_checkpointStore = new(DefaultIndex, DefaultIndex.Handler);
	}

	public async ValueTask HandleAsync(SystemReady message, CancellationToken token) {
		DefaultIndex.Init();
		_subscription = new(_publisher, _checkpointStore, DefaultIndex.Handler);
		await _subscription.Subscribe(
			id => Log.Information("Index subscription {Subscription} subscribed", id),
			(id, reason, ex) => Log.Warning(ex, "Index subscription {Subscription} dropped {Reason}", id, reason),
			token
		);
	}

	public async ValueTask HandleAsync(BecomeShuttingDown message, CancellationToken token) {
		DefaultIndex.Handler.Commit(false);
		await Task.Delay(100, token);
		_db.Close();
		// await _subscription.Unsubscribe(id => Log.Information("Index subscription {Subscription} unsubscribed", id), token);
	}
}
