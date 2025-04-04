using System;
using System.Collections.Generic;
using System.IO;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
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

		private void CreateChunk(int numEvents, bool completed, bool scavenged,
			out TFChunk chunk, out List<long> logicalPositions, out List<PosMap> posMap) {
			if (scavenged && !completed)
				throw new ArgumentException("scavenged chunk must be completed");

			chunk = TFChunkHelper.CreateNewChunk(Path.Combine(PathName, $"{Guid.NewGuid()}.chunk"), 4096, scavenged);

			logicalPositions = new();
			posMap = scavenged ? new() : null;

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
				chunk.CompleteScavenge(posMap);
			else if (completed)
				chunk.Complete();
		}

		[Test]
		[Repeat(10)]
		public void return_correct_positions_for_an_incomplete_unscavenged_chunk() {
			var numEvents = _random.Next(10, 20);
			CreateChunk(
				numEvents: numEvents,
				completed: false,
				scavenged: false,
				out var chunk,
				out var logPositions,
				out var posMap);

			Assert.AreEqual(numEvents, logPositions.Count);
			foreach (var logPos in logPositions)
				Assert.AreEqual(ChunkHeader.Size + logPos, chunk.GetActualRawPosition(logPos));
			Assert.IsNull(posMap);
		}

		[Test]
		[Repeat(10)]
		public void return_correct_positions_for_a_complete_unscavenged_chunk() {
			var numEvents = _random.Next(10, 20);
			CreateChunk(
				numEvents: numEvents,
				completed: true,
				scavenged: false,
				out var chunk,
				out var logPositions,
				out var posMap);

			Assert.AreEqual(numEvents, logPositions.Count);
			foreach (var logPos in logPositions)
				Assert.AreEqual(ChunkHeader.Size + logPos, chunk.GetActualRawPosition(logPos));
			Assert.IsNull(posMap);
		}

		[Test]
		[Repeat(10)]
		public void return_correct_positions_for_a_scavenged_chunk() {
			var numEvents = _random.Next(10, 20);
			CreateChunk(
				numEvents: numEvents,
				completed: true,
				scavenged: true,
				out var chunk,
				out var logPositions,
				out var posMap);

			Assert.AreEqual(numEvents, logPositions.Count);
			Assert.AreEqual(numEvents, posMap.Count);
			for (int i = 0; i < numEvents; i++) {
				Assert.AreEqual(posMap[i].LogPos, logPositions[i]);
				Assert.AreEqual(ChunkHeader.Size + posMap[i].ActualPos, chunk.GetActualRawPosition(logPositions[i]));
			}
		}

		[Test]
		public void return_minus_one_for_positions_that_are_outside_the_range_of_an_unscavenged_chunk() {
			CreateChunk(
				numEvents: 1,
				completed: true,
				scavenged: false,
				out var chunk,
				out var logPositions,
				out var posMap);

			Assert.AreEqual(1, logPositions.Count);
			Assert.IsNull(posMap);

			Assert.AreEqual(chunk.LogicalDataSize, chunk.PhysicalDataSize);
			Assert.AreEqual(ChunkHeader.Size + chunk.LogicalDataSize - 1, chunk.GetActualRawPosition(chunk.LogicalDataSize - 1));
			Assert.AreEqual(-1, chunk.GetActualRawPosition(chunk.LogicalDataSize));
			Assert.AreEqual(-1, chunk.GetActualRawPosition(chunk.LogicalDataSize + 1));
		}

		[Test]
		public void return_minus_one_for_positions_that_do_not_exist_in_a_scavenged_chunk() {
			CreateChunk(
				numEvents: 1,
				completed: true,
				scavenged: true,
				out var chunk,
				out var logPositions,
				out var posMap);

			Assert.AreEqual(1, logPositions.Count);
			Assert.AreEqual(1, posMap.Count);

			Assert.AreEqual(ChunkHeader.Size + posMap[0].ActualPos, chunk.GetActualRawPosition(logPositions[0]));
			Assert.AreEqual(-1, chunk.GetActualRawPosition(logPositions[0] + 1));
		}

		[Test]
		public void throw_argument_out_of_range_exception_for_negative_positions() {
			CreateChunk(
				numEvents: 1,
				completed: false,
				scavenged: false,
				out var chunk,
				out _,
				out _);

			Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetActualRawPosition(-1));
		}
	}
}
