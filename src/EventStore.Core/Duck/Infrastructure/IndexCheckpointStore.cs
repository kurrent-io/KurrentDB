// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Log;
using EventStore.Core.Duck.Default;
using Eventuous.Subscriptions.Checkpoints;
using Serilog;

namespace EventStore.Core.Duck.Infrastructure;

public class IndexCheckpointStore<TStreamId>(DefaultIndex<TStreamId> defaultIndex, DefaultIndexHandler<TStreamId> handler) : ICheckpointStore {
	static readonly ILogger Log = Serilog.Log.Logger.ForContext("IndexCheckpointStore");

	public ValueTask<Checkpoint> GetLastCheckpoint(string checkpointId, CancellationToken cancellationToken) {
		var lastPosition = defaultIndex.GetLastPosition();
		Log.Information("Starting from {LastPosition}", lastPosition);
		return ValueTask.FromResult(new Checkpoint(checkpointId, lastPosition));
	}

	public async ValueTask<Checkpoint> StoreCheckpoint(Checkpoint checkpoint, bool force, CancellationToken cancellationToken) {
		if (!handler.NeedsCommitting) return checkpoint;

		while (true) {
			try {
				handler.Commit();
				defaultIndex.StreamIndex.Commit();
				break;
			} catch (Exception e) {
				Log.Warning("Unable to commit {Checkpoint}, will retry. Error: {Error}", checkpoint, e.Message);
				await Task.Delay(100, cancellationToken);
			}
		}

		return checkpoint;
	}
}
