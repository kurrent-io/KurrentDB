// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Collections.Generic;
using EventStore.Core.Data;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_truncating_few_chunks_with_index_on_disk<TLogFormat, TStreamId> : TruncateScenario<TLogFormat, TStreamId> {
	private EventRecord _event4;

	private string _chunk0;
	private string _chunk1;
	private string _chunk2;
	private string _chunk3;

	public when_truncating_few_chunks_with_index_on_disk()
		: base(maxEntriesInMemTable: 3) {
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteSingleEvent("ES", 0, new string('.', 4000), token: token);
		await WriteSingleEvent("ES", 1, new string('.', 4000), token: token);
		await WriteSingleEvent("ES", 2, new string('.', 4000), retryOnFail: true, token: token); // ptable 1, chunk 1
		_event4 = await WriteSingleEvent("ES", 3, new string('.', 4000), token: token);
		await WriteSingleEvent("ES", 4, new string('.', 4000), retryOnFail: true, token: token); // chunk 2
		await WriteSingleEvent("ES", 5, new string('.', 4000), token: token); // ptable 2
		await WriteSingleEvent("ES", 6, new string('.', 4000), retryOnFail: true, token: token); // chunk 3

		TruncateCheckpoint = _event4.LogPosition;

		_chunk0 = GetChunkName(0);
		_chunk1 = GetChunkName(1);
		_chunk2 = GetChunkName(2);
		_chunk3 = GetChunkName(3);

		Assert.IsTrue(File.Exists(_chunk0));
		Assert.IsTrue(File.Exists(_chunk1));
		Assert.IsTrue(File.Exists(_chunk2));
		Assert.IsTrue(File.Exists(_chunk3));
	}

	private string GetChunkName(int chunkNumber) {
		var allVersions = Db.Manager.FileSystem.LocalNamingStrategy.GetAllVersionsFor(chunkNumber);
		Assert.AreEqual(1, allVersions.Length);
		return allVersions[0];
	}

	[Test]
	public void checksums_should_be_equal_to_ack_checksum() {
		Assert.AreEqual(TruncateCheckpoint, WriterCheckpoint.Read());
		Assert.AreEqual(TruncateCheckpoint, ChaserCheckpoint.Read());
	}

	[Test]
	public void truncated_chunks_should_be_deleted() {
		Assert.IsFalse(File.Exists(_chunk2));
		Assert.IsFalse(File.Exists(_chunk3));
	}

	[Test]
	public async Task not_truncated_chunks_should_survive() {
		var chunks = await Db.Manager.FileSystem.GetChunks().ToArrayAsync();
		Assert.AreEqual(2, chunks.Length);

		Assert.AreEqual(_chunk0, GetChunkName(0));
		Assert.AreEqual(_chunk0, chunks[0].FileName);

		Assert.AreEqual(_chunk1, GetChunkName(1));
		Assert.AreEqual(_chunk1, chunks[1].FileName);
	}
}
