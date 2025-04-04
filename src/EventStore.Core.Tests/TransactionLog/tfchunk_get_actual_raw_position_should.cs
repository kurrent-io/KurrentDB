// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class tfchunk_get_actual_raw_position_should<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private readonly TStreamId _streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
	private readonly TStreamId _eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
	private readonly IRecordFactory<TStreamId> _recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
	private readonly Random _random = new();

	private IPrepareLogRecord<TStreamId> CreateRecord(long logPosition, int dataSize) {
		return LogRecord.Prepare(_recordFactory, logPosition, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 1,
			PrepareFlags.None, _eventTypeId, new byte[dataSize], Array.Empty<byte>(), new DateTime(2000, 1, 1, 12, 0, 0));
	}

	private async ValueTask<TFChunk> CreateChunk(int numEvents, bool completed, bool scavenged,
		List<long> logicalPositions, List<PosMap> posMap) {
		if (scavenged && !completed)
			throw new ArgumentException("scavenged chunk must be completed");

		var chunk = TFChunkHelper.CreateNewChunk(Path.Combine(PathName, $"{Guid.NewGuid()}.chunk"), 4096, scavenged);

		var actualPos = 0;
		for (int i = 0; i < numEvents; i++) {
			long logicalPos;
			if (!scavenged) {
				logicalPos = actualPos;
			} else {
				logicalPos = actualPos + _random.Next(0, 100);
				posMap.Add(new PosMap(logicalPos, actualPos));
			}
			logicalPositions.Add(logicalPos);

			var result = chunk.TryAppend(CreateRecord(chunk.ChunkHeader.GetGlobalLogPosition(logicalPos), _random.Next(10, 100)));
			Assert.True(result.Success);
			actualPos = (int)result.NewPosition;
		}

		chunk.Flush();

		if (scavenged)
			await chunk.CompleteScavenge(posMap, CancellationToken.None);
		else if (completed)
			chunk.Complete();

		return chunk;
	}

	[Test]
	[Repeat(10)]
	public async Task return_correct_positions_for_an_incomplete_unscavenged_chunk() {
		var numEvents = _random.Next(10, 20);
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: numEvents,
			completed: false,
			scavenged: false,
			logPositions,
			posMap);

		Assert.AreEqual(numEvents, logPositions.Count);
		foreach (var logPos in logPositions)
			Assert.AreEqual(ChunkHeader.Size + logPos, chunk.GetActualRawPosition(logPos));
		Assert.IsEmpty(posMap);
	}

	[Test]
	[Repeat(10)]
	public async Task return_correct_positions_for_a_complete_unscavenged_chunk() {
		var numEvents = _random.Next(10, 20);
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: numEvents,
			completed: true,
			scavenged: false,
			logPositions,
			posMap);

		Assert.AreEqual(numEvents, logPositions.Count);
		foreach (var logPos in logPositions)
			Assert.AreEqual(ChunkHeader.Size + logPos, chunk.GetActualRawPosition(logPos));
		Assert.IsEmpty(posMap);
	}

	[Test]
	[Repeat(10)]
	public async Task return_correct_positions_for_a_scavenged_chunk() {
		var numEvents = _random.Next(10, 20);
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: numEvents,
			completed: true,
			scavenged: true,
			logPositions,
			posMap);

		Assert.AreEqual(numEvents, logPositions.Count);
		Assert.AreEqual(numEvents, posMap.Count);
		for (int i = 0; i < numEvents; i++) {
			Assert.AreEqual(posMap[i].LogPos, logPositions[i]);
			Assert.AreEqual(ChunkHeader.Size + posMap[i].ActualPos, chunk.GetActualRawPosition(logPositions[i]));
		}
	}

	[Test]
	public async Task return_minus_one_for_positions_that_are_outside_the_range_of_an_unscavenged_chunk() {
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: 1,
			completed: true,
			scavenged: false,
			logPositions,
			posMap);

		Assert.AreEqual(1, logPositions.Count);
		Assert.IsEmpty(posMap);

		Assert.AreEqual(chunk.LogicalDataSize, chunk.PhysicalDataSize);
		Assert.AreEqual(ChunkHeader.Size + chunk.LogicalDataSize - 1, chunk.GetActualRawPosition(chunk.LogicalDataSize - 1));
		Assert.AreEqual(-1, chunk.GetActualRawPosition(chunk.LogicalDataSize));
		Assert.AreEqual(-1, chunk.GetActualRawPosition(chunk.LogicalDataSize + 1));
	}

	[Test]
	public async Task return_minus_one_for_positions_that_do_not_exist_in_a_scavenged_chunk() {
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: 1,
			completed: true,
			scavenged: true,
			logPositions,
			posMap);

		Assert.AreEqual(1, logPositions.Count);
		Assert.AreEqual(1, posMap.Count);

		Assert.AreEqual(ChunkHeader.Size + posMap[0].ActualPos, chunk.GetActualRawPosition(logPositions[0]));
		Assert.AreEqual(-1, chunk.GetActualRawPosition(logPositions[0] + 1));
	}

	[Test]
	public async Task throw_argument_out_of_range_exception_for_negative_positions() {
		var logPositions = new List<long>();
		var posMap = new List<PosMap>();

		var chunk = await CreateChunk(
			numEvents: 1,
			completed: false,
			scavenged: false,
			logPositions,
			posMap);

		Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetActualRawPosition(-1));
	}
}
