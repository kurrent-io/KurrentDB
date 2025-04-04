// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.Services.Storage;

// The resulting scavenger is used for one continuous run. If it is cancelled or
// completed then starting scavenge again will instantiate another scavenger
// with a different id.
public class ScavengerFactory(Func<ClientMessage.ScavengeDatabase, ITFChunkScavengerLog, ILogger, IScavenger> create) {
	public IScavenger Create(ClientMessage.ScavengeDatabase message, ITFChunkScavengerLog scavengerLogger, ILogger logger) => create(message, scavengerLogger, logger);
}

public class OldScavenger<TStreamId>(
	bool alwaysKeepScaveged,
	bool mergeChunks,
	int startFromChunk,
	TFChunkScavenger<TStreamId> tfChunkScavenger)
	: IScavenger {
	public string ScavengeId => tfChunkScavenger.ScavengeId;

	public void Dispose() {
	}

	public Task<ScavengeResult> ScavengeAsync(CancellationToken cancellationToken) {
		return tfChunkScavenger.Scavenge(
			alwaysKeepScavenged: alwaysKeepScaveged,
			mergeChunks: mergeChunks,
			startFromChunk: startFromChunk,
			ct: cancellationToken);
	}
}
