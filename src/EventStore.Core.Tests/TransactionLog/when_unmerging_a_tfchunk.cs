// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.DbAccess;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_unmerging_a_tfchunk<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private string Filename => Path.Combine(PathName, Guid.NewGuid().ToString());

	[Test]
	public async Task throws_when_unmerging_a_single_logical_chunk() {
		using var chunk = await TFChunkHelper.CreateNewChunk(Filename, chunkStartNumber: 2, chunkEndNumber: 2, isScavenged: true);
		await chunk.CompleteScavenge(Array.Empty<PosMap>(), CancellationToken.None);

		Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await foreach (var _ in chunk.UnmergeAsync(CancellationToken.None)) { }
		});
	}


	[Test]
	public async Task throws_when_unmerging_a_non_scavenged_chunk() {
		using var chunk = await TFChunkHelper.CreateNewChunk(Filename, chunkStartNumber: 2, chunkEndNumber: 3, isScavenged: false);
		await chunk.Complete(CancellationToken.None);

		Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await foreach (var _ in chunk.UnmergeAsync(CancellationToken.None)) { }
		});
	}

	[Test]
	public async Task throws_when_unmerging_an_incomplete_chunk() {
		using var chunk = await TFChunkHelper.CreateNewChunk(Filename, chunkStartNumber: 2, chunkEndNumber: 3, isScavenged: true);

		Assert.ThrowsAsync<InvalidOperationException>(async () => {
			await foreach (var _ in chunk.UnmergeAsync(CancellationToken.None)) { }
		});
	}

	[Test]
	public async Task can_unmerge_a_merged_chunk() {
		var chunkSize = 4096;
		using var chunk = await TFChunkHelper.CreateNewChunk(
			Filename,
			chunkStartNumber: 2,
			chunkEndNumber: 5,
			chunkSize: chunkSize,
			isScavenged: true);

		var writer = new ScavengedChunkWriter(chunk);
		var recordPositions = new long[] {
			3 * chunkSize,
			3 * chunkSize + 200,
			3 * chunkSize + 400,
			5 * chunkSize,
			5 * chunkSize + 200,
			5 * chunkSize + 400
		};

		var token = CancellationToken.None;

		await WriteRecords(writer, recordPositions, token);
		await writer.Complete(token);

		var chunkNumberCounter = 2;
		var readRecordPositions = new List<long>();
		await foreach (var unmerged in chunk.UnmergeAsync(token)) {
			Assert.AreEqual(chunkNumberCounter, unmerged.ChunkHeader.ChunkStartNumber);
			Assert.AreEqual(chunkNumberCounter, unmerged.ChunkHeader.ChunkEndNumber);
			var records = await ReadAllRecords(unmerged as TFChunk, token).ToArrayAsync(token);
			Assert.True(records.All(logPosition => logPosition / chunkSize == chunkNumberCounter));
			readRecordPositions.AddRange(records);
			chunkNumberCounter++;
		}

		Assert.AreEqual(recordPositions, readRecordPositions);
		Assert.AreEqual(6, chunkNumberCounter);
	}

	private async ValueTask WriteRecords(ScavengedChunkWriter writer, long[] logPositions, CancellationToken token) {
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		foreach (var logPosition in logPositions) {
			var record = LogRecord.Prepare(
				factory: recordFactory,
				logPosition: logPosition,
				correlationId: Guid.NewGuid(),
				eventId: Guid.NewGuid(),
				transactionPos: 0,
				transactionOffset: 0,
				eventStreamId: streamId,
				expectedVersion: 0,
				flags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted,
				eventType: eventTypeId,
				data: Array.Empty<byte>(),
				metadata: Array.Empty<byte>());

			await writer.WriteRecord(record, token);
		}
	}

	private async IAsyncEnumerable<long> ReadAllRecords(TFChunk chunk, [EnumeratorCancellation] CancellationToken token) {
		var result = await chunk.TryReadClosestForward(0L, token);
		while (result.Success) {
			yield return result.LogRecord.LogPosition;
			result = await chunk.TryReadClosestForward(result.NextPosition, token);
		}
	}
}
