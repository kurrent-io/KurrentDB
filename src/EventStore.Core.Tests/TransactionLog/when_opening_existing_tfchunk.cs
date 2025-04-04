// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture]
public class when_opening_existing_tfchunk : SpecificationWithFilePerTestFixture {
	private TFChunk _chunk;
	private TFChunk _testChunk;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_chunk = await TFChunkHelper.CreateNewChunk(Filename);
		await _chunk.Complete(CancellationToken.None);
		_testChunk = await TFChunk.FromCompletedFile(new ChunkLocalFileSystem(Path.GetDirectoryName(Filename)), Filename, true, false,
			reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default);
	}

	[TearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		_testChunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void the_chunk_is_not_cached() {
		Assert.IsFalse(_testChunk.IsCached);
	}

	[Test]
	public void the_chunk_is_readonly() {
		Assert.IsTrue(_testChunk.IsReadOnly);
	}

	[Test]
	public void append_throws_invalid_operation_exception() {
		Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await _testChunk.TryAppend(new CommitLogRecord(0, Guid.NewGuid(), 0, DateTime.UtcNow, 0), CancellationToken.None));
	}

	[Test]
	public void flush_does_not_throw_any_exception() {
		Assert.DoesNotThrowAsync(async () => await _testChunk.Flush(CancellationToken.None));
	}
}
