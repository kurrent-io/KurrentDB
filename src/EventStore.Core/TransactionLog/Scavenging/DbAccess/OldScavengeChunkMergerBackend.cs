// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class OldScavengeChunkMergerBackend : IChunkMergerBackend {
	private readonly ILogger _logger;
	private readonly TFChunkDb _db;

	public OldScavengeChunkMergerBackend(ILogger logger, TFChunkDb db) {
		_logger = logger;
		_db = db;
	}

	public ValueTask MergeChunks(
		ITFChunkScavengerLog scavengerLogger,
		Throttle throttle,
		CancellationToken cancellationToken) {

		// todo: if time permits we could look in more detail at this implementation and see if it
		// could be improved or replaced.
		// todo: if time permits we could stop after the chunk with the scavenge point
		// todo: if time permits we could start with the minimum executed chunk this scavenge
		// todo: if time permits we could add some way of checkpointing during the merges
		// todo: move MergePhase to non-generic TFChunkScavenger
		return TFChunkScavenger<string>.MergePhase(
			logger: _logger,
			db: _db,
			maxChunkDataSize: _db.Config.ChunkSize,
			scavengerLog: scavengerLogger,
			throttle: throttle,
			ct: cancellationToken);
	}
}
