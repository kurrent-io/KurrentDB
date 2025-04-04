// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkManagerForChunkRemover : IChunkManagerForChunkRemover {
	private readonly TFChunkManager _manager;

	public ChunkManagerForChunkRemover(TFChunkManager manager) {
		_manager = manager;
	}

	public ValueTask<bool> SwitchInChunks(IReadOnlyList<string> locators, CancellationToken token) =>
		_manager.SwitchInCompletedChunks(locators, token);
}
