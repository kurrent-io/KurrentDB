// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Core.Transforms;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.TransactionLog;

[TestFixture]
public class when_opening_existing_ongoing_tfchunk : SpecificationWithFilePerTestFixture {
	private TFChunk _chunk;
	private TFChunk _testChunk;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_chunk = await TFChunkHelper.CreateNewChunk(Filename, chunkSize: 10 * 1024);
		_chunk.TryClose();
		_testChunk = await TFChunk.FromOngoingFile(
			fileSystem: new ChunkLocalFileSystem(Path.GetDirectoryName(Filename)),
			filename: Filename,
			writePosition: 0,
			unbuffered: false,
			writethrough: false,
			reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default,
			token: CancellationToken.None);
	}

	[OneTimeTearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		_testChunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void the_chunk_is_cached() {
		Assert.IsTrue(_testChunk.IsCached);
	}

	[Test]
	public void the_chunk_is_not_readonly() {
		Assert.IsFalse(_testChunk.IsReadOnly);
	}

	[Test]
	// a flush before write can be triggered in practice when joining a cluster and the first
	// replication message (8k) does not contain a full transaction.
	public async Task can_flush_and_then_write() {
		await _testChunk.Flush(CancellationToken.None);
		var result = await _testChunk.TryAppend(new CommitLogRecord(0, Guid.NewGuid(), 0, DateTime.UtcNow, 0), CancellationToken.None);
		Assert.IsTrue(result.Success);
	}
}
