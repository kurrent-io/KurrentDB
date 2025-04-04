// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Scavenging;

[TestFixture]
public class scavenged_chunk : SpecificationWithFile {
	[Test]
	public async Task is_fully_resident_in_memory_when_cached() {
		var map = new List<PosMap>();
		var chunk = await TFChunk.CreateNew(new ChunkLocalFileSystem(path: ""), Filename, 1024 * 1024, 0, 0, true, false, false, false,
			false,
			new TFChunkTracker.NoOp(),
			DbTransformManager.Default,
			CancellationToken.None);
		long logPos = 0;
		for (int i = 0, n = ChunkFooter.Size / PosMap.FullSize + 1; i < n; ++i) {
			map.Add(new PosMap(logPos, (int)logPos));
			var res = await chunk.TryAppend(LogRecord.Commit(logPos, Guid.NewGuid(), logPos, 0), CancellationToken.None);
			Assert.IsTrue(res.Success);
			logPos = res.NewPosition;
		}

		await chunk.CompleteScavenge(map, CancellationToken.None);

		await chunk.CacheInMemory(CancellationToken.None);

		Assert.IsTrue(chunk.IsCached);

		var last = await chunk.TryReadLast(CancellationToken.None);
		Assert.IsTrue(last.Success);
		Assert.AreEqual(map[map.Count - 1].ActualPos, last.LogRecord.LogPosition);

		chunk.MarkForDeletion();
		chunk.WaitForDestroy(1000);
	}
}
