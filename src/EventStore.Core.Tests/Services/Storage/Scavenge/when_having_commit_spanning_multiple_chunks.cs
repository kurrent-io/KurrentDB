// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_having_commit_spanning_multiple_chunks<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private List<ILogRecord> _survivors;
	private List<ILogRecord> _scavenged;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_survivors = new List<ILogRecord>();
		_scavenged = new List<ILogRecord>();

		var (s1StreamId, _) = await GetOrReserve("s1", token);
		var (eventTypeId, _) = await GetOrReserveEventType("event-type", token);
		var transPos = Writer.Position;

		for (int i = 0; i < 10; ++i) {
			var r = LogRecord.Prepare(_recordFactory, Writer.Position,
				Guid.NewGuid(),
				Guid.NewGuid(),
				transPos,
				i,
				s1StreamId,
				i == 0 ? -1 : -2,
				PrepareFlags.Data | (i == 9 ? PrepareFlags.TransactionEnd : PrepareFlags.None),
				eventTypeId,
				new byte[3],
				new byte[3]);
			Assert.IsTrue(await Writer.Write(r, token) is (true, _));
			await Writer.CompleteChunk(token);
			await Writer.AddNewChunk(token: token);

			_scavenged.Add(r);
		}

		var r2 = await WriteCommit(transPos, "s1", 0, token);
		_survivors.Add(r2);

		await Writer.CompleteChunk(token);
		await Writer.AddNewChunk(token: token);

		var r3 = await WriteDeletePrepare("s1", token);
		_survivors.Add(r3);

		await Writer.CompleteChunk(token);
		await Writer.AddNewChunk(token: token);

		var r4 = await WriteDeleteCommit(r3, token);
		_survivors.Add(r4);

		await Writer.CompleteChunk(token);
		await Writer.AddNewChunk(token: token);

		Scavenge(completeLast: false, mergeChunks: true);

		Assert.AreEqual(13, _survivors.Count + _scavenged.Count);
	}

	[Test]
	public async Task all_chunks_are_merged_and_scavenged() {
		foreach (var rec in _scavenged) {
			var chunk = await Db.Manager.GetInitializedChunkFor(rec.LogPosition, CancellationToken.None);
			Assert.IsTrue(await chunk.TryReadAt(rec.LogPosition, couldBeScavenged: true, CancellationToken.None) is
				{ Success: false });
		}

		foreach (var rec in _survivors) {
			var chunk = await Db.Manager.GetInitializedChunkFor(rec.LogPosition, CancellationToken.None);
			var res = await chunk.TryReadAt(rec.LogPosition, couldBeScavenged: false, CancellationToken.None);
			Assert.IsTrue(res.Success);
			Assert.AreEqual(rec, res.LogRecord);
		}
	}
}
