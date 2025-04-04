// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.Transforms.Identity;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

public abstract class TruncateScenario<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	protected TFChunkDbTruncator Truncator;
	protected long TruncateCheckpoint = long.MinValue;

	protected TruncateScenario(int maxEntriesInMemTable = 100, int metastreamMaxCount = 1)
		: base(maxEntriesInMemTable, metastreamMaxCount) {
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		if (TruncateCheckpoint == long.MinValue)
			throw new InvalidOperationException("AckCheckpoint must be set in WriteTestScenario.");

		OnBeforeTruncating();

		// need to close db before truncator can delete files

		ReadIndex.Close();
		ReadIndex.Dispose();

		TableIndex.Close(removeFiles: false);

		await Db.DisposeAsync();

		var truncator = new TFChunkDbTruncator(Db.Config, new ChunkLocalFileSystem(Db.Config.Path), static _ => new IdentityChunkTransformFactory());
		await truncator.TruncateDb(TruncateCheckpoint, CancellationToken.None);
	}

	protected virtual void OnBeforeTruncating() {
	}
}
