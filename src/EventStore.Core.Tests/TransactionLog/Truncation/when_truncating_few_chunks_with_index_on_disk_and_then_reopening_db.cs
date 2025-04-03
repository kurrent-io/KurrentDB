// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog.Truncation;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_truncating_few_chunks_with_index_on_disk_and_then_reopening_db<TLogFormat, TStreamId> : TruncateAndReOpenDbScenario<TLogFormat, TStreamId> {
	private EventRecord _event1;
	private EventRecord _event2;
	private EventRecord _event3;
	private EventRecord _event4;
	private EventRecord _event7;

	private string _chunk0;
	private string _chunk1;
	private string _chunk2;
	private string _chunk3;

	public when_truncating_few_chunks_with_index_on_disk_and_then_reopening_db()
		: base(maxEntriesInMemTable: 3) {
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_event1 = await WriteSingleEvent("ES", 0, new string('.', 4000), token: token); // chunk 0
		_event2 = await WriteSingleEvent("ES", 1, new string('.', 4000), token: token);
		_event3 = await WriteSingleEvent("ES", 2, new string('.', 4000), retryOnFail: true, token: token); // ptable 1, chunk 1
		_event4 = await WriteSingleEvent("ES", 3, new string('.', 4000), token: token);
		await WriteSingleEvent("ES", 4, new string('.', 4000), retryOnFail: true, token: token); // chunk 2
		await WriteSingleEvent("ES", 5, new string('.', 4000), token: token); // ptable 2
		_event7 = await WriteSingleEvent("ES", 6, new string('.', 4000), retryOnFail: true, token: token); // chunk 3

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

	[Test]
	public async Task read_one_by_one_doesnt_return_truncated_records() {
		var res = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual(_event1, res.Record);
		res = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual(_event2, res.Record);
		res = await ReadIndex.ReadEvent("ES", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, res.Result);
		Assert.AreEqual(_event3, res.Record);

		res = await ReadIndex.ReadEvent("ES", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, res.Result);
		Assert.IsNull(res.Record);
		res = await ReadIndex.ReadEvent("ES", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, res.Result);
		Assert.IsNull(res.Record);
		res = await ReadIndex.ReadEvent("ES", 5, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, res.Result);
		Assert.IsNull(res.Record);
		res = await ReadIndex.ReadEvent("ES", 6, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, res.Result);
		Assert.IsNull(res.Record);
		res = await ReadIndex.ReadEvent("ES", 7, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, res.Result);
		Assert.IsNull(res.Record);
	}

	[Test]
	public async Task read_stream_forward_doesnt_return_truncated_records() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event1, records[0]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[2]);
	}

	[Test]
	public async Task read_stream_backward_doesnt_return_truncated_records() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", -1, 100, CancellationToken.None);
		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event1, records[2]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[0]);
	}

	[Test]
	public async Task read_all_returns_only_survived_events() {
		var res = await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None);
		var records = res.EventRecords()
			.Select(r => r.Event)
			.ToArray();

		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event1, records[0]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[2]);
	}

	[Test]
	public async Task read_all_backward_doesnt_return_truncated_records() {
		var res = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None));
		var records = res.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event1, records[2]);
		Assert.AreEqual(_event2, records[1]);
		Assert.AreEqual(_event3, records[0]);
	}

	[Test]
	public async Task read_all_backward_from_last_truncated_record_returns_no_records() {
		var pos = new TFPos(_event7.LogPosition, _event3.LogPosition);
		var res = await ReadIndex.ReadAllEventsForward(pos, 100, CancellationToken.None);
		var records = res.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(0, records.Length);
	}
}
