// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.TransactionLog;

[TestFixture]
public class when_reading_cached_empty_scavenged_tfchunk : SpecificationWithFilePerTestFixture {
	private TFChunk _chunk;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_chunk = await TFChunkHelper.CreateNewChunk(Filename, isScavenged: true);
		await _chunk.CompleteScavenge(new PosMap[0], CancellationToken.None);
		await _chunk.CacheInMemory(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public async Task no_record_at_exact_position_can_be_read() {
		Assert.IsTrue(await _chunk.TryReadAt(0, couldBeScavenged: true, CancellationToken.None) is { Success: false });
	}

	[Test]
	public async Task no_record_can_be_read_as_first_record() {
		Assert.IsFalse((await _chunk.TryReadFirst(CancellationToken.None)).Success);
	}

	[Test]
	public async Task no_record_can_be_read_as_closest_forward_record() {
		Assert.IsTrue(await _chunk.TryReadClosestForward(0, CancellationToken.None) is { Success: false });
	}

	[Test]
	public async Task no_record_can_be_read_as_closest_backward_record() {
		Assert.IsFalse((await _chunk.TryReadClosestBackward(0, CancellationToken.None)).Success);
	}

	[Test]
	public async Task no_record_can_be_read_as_last_record() {
		Assert.IsFalse((await _chunk.TryReadLast(CancellationToken.None)).Success);
	}
}
