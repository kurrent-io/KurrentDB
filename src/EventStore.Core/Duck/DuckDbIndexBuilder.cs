// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Duck.Default;
using EventStore.Core.Duck.Infrastructure;
using EventStore.Core.Services.Storage.ReaderIndex;
using static EventStore.Core.Messages.SystemMessage;

namespace EventStore.Core.Duck;

class DuckDbIndexBuilder : IHandle<SystemReady>, IAsyncHandle<BecomeShuttingDown> {
	SecondaryIndexSubscription _subscription;
	readonly DuckDbDataSource _db;
	readonly IPublisher _publisher;

	internal DefaultIndex DefaultIndex { get; }

	[Experimental("DuckDBNET001")]
	public DuckDbIndexBuilder(DuckDbDataSource db, IPublisher publisher, IReadIndex<string> index) {
		_publisher = publisher;
		_db = db;
		DefaultIndex = new(_db, index);
		new InlineFunctions(_db, publisher).Run();
	}

	public void Handle(SystemReady message) {
		DefaultIndex.Init();
		_subscription = new(_publisher, DefaultIndex);
		_subscription.Subscribe();
	}

	public async ValueTask HandleAsync(BecomeShuttingDown message, CancellationToken cancellationToken) {
		await _subscription.DisposeAsync();
		DefaultIndex.Dispose();
		_db.Dispose();
	}
}
