// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;
using static EventStore.Core.TransactionLog.ITransactionFileTracker;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public sealed class TFChunkTrackerTests : IDisposable {
	const long WriterCheckpoint = 4_500;
	const int ChunkSize = 1_000;

	private readonly TFChunkTracker _sut;
	private readonly TestMeterListener<long> _listener;
	private readonly TestMeterListener<double> _doubleListener;
	private readonly FakeClock _clock = new();

	public TFChunkTrackerTests() {
		var meter = new Meter($"{typeof(TFChunkTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		_doubleListener = new TestMeterListener<double>(meter);
		var byteMetric = new CounterMetric(meter, "eventstore-io", unit: "bytes", legacyNames: false);
		var eventMetric = new CounterMetric(meter, "eventstore-io", unit: "events", legacyNames: false);
		var writerCheckpoint = new InMemoryCheckpoint(WriterCheckpoint);

		var readTag = new KeyValuePair<string, object>("activity", "read");
		_sut = new TFChunkTracker(
			readDistribution: new LogicalChunkReadDistributionMetric(meter, "chunk-read-distribution", writerCheckpoint, ChunkSize),
			readDurationMetric: new DurationMetric(meter, "record-read-duration", legacyNames: false, _clock),
			readBytes: new CounterSubMetric(byteMetric, [readTag]),
			readEvents: new CounterSubMetric(eventMetric, [readTag]));
	}

	public void Dispose() {
		_listener.Dispose();
		_doubleListener.Dispose();
	}

	[Fact]
	public void can_observe_prepare_log() {
		var prepare = CreatePrepare(
			data: new byte[5],
			meta: new byte[5]);

		_sut.OnRead(_clock.Now, prepare, Source.Unknown);
		_listener.Observe();

		AssertEventsRead(1);
		AssertBytesRead(10);
	}

	[Fact]
	public void disregard_system_log() {
		var system = CreateSystemRecord();
		_sut.OnRead(_clock.Now, system, Source.Unknown);
		_listener.Observe();

		AssertEventsRead(0);
		AssertBytesRead(0);
	}

	[Fact]
	public void disregard_commit_log() {
		var system = CreateCommit();
		_sut.OnRead(_clock.Now, system, Source.Unknown);
		_listener.Observe();

		AssertEventsRead(0);
		AssertBytesRead(0);
	}

	[Theory]
	[InlineData(Source.ChunkCache)]
	public void records_record_read_duration(Source source) {
		var prepare = CreatePrepare(
			data: new byte[5],
			meta: new byte[5]);

		var start = _clock.Now;
		_clock.AdvanceSeconds(3);

		// when
		_sut.OnRead(start, prepare, source);

		// then
		_doubleListener.Observe();
		var actual = _doubleListener.RetrieveMeasurements("record-read-duration-seconds");
		Assert.Collection(
			actual,
			m => {
				Assert.Equal(3, m.Value);
				Assert.Collection(
					m.Tags,
					t => {
						Assert.Equal("source", t.Key);
						Assert.Equal(source, t.Value);
					},
					t => {
						Assert.Equal("user", t.Key);
						Assert.Equal("", t.Value);
					});
			});
	}

	[Theory]
	[InlineData(5_500, -1)]
	[InlineData(4_501, 0)]
	[InlineData(4_500, 0)]
	[InlineData(4_000, 0)]
	[InlineData(3_999, 1)]
	[InlineData(3_000, 1)]
	[InlineData(2_000, 2)]
	[InlineData(1_000, 3)]
	[InlineData(999, 4)]
	[InlineData(1, 4)]
	[InlineData(0, 4)]
	public void records_read_distribution(long logPosition, long expectedChunk) {
		_sut.OnRead(
			_clock.Now,
			CreatePrepare(data: new byte[5], meta: new byte[5], logPosition: logPosition),
			Source.Unknown);

		_listener.Observe();
		var actual = _listener.RetrieveMeasurements("chunk-read-distribution");
		Assert.Collection(
			actual,
			m => {
				Assert.Equal(expectedChunk, m.Value);
				Assert.Empty(m.Tags);
			});
	}

	private void AssertEventsRead(long? expectedEventsRead) =>
		AssertMeasurements("eventstore-io-events", expectedEventsRead);

	private void AssertBytesRead(long? expectedBytesRead) =>
		AssertMeasurements("eventstore-io-bytes", expectedBytesRead);

	private void AssertMeasurements(string instrumentName, long? expectedValue) {
		var actual = _listener.RetrieveMeasurements(instrumentName);

		if (expectedValue is null) {
			Assert.Empty(actual);
		} else {
			Assert.Collection(
				actual,
				m => {
					Assert.Equal(expectedValue, m.Value);
					Assert.Collection(m.Tags.ToArray(), t => {
						Assert.Equal("activity", t.Key);
						Assert.Equal("read", t.Value);
					});
				});
		}
	}

	private static PrepareLogRecord CreatePrepare(byte[] data, byte[] meta, long logPosition = 42) {
		return new PrepareLogRecord(logPosition, Guid.NewGuid(), Guid.NewGuid(), 42, 42, "tests", null, 42, DateTime.Now,
			PrepareFlags.Data, "type-test", null, data, meta);
	}

	private static SystemLogRecord CreateSystemRecord() {
		return new SystemLogRecord(42, DateTime.Now, SystemRecordType.Epoch, SystemRecordSerialization.Binary, Array.Empty<byte>());
	}

	private static CommitLogRecord CreateCommit() {
		return new CommitLogRecord(42, Guid.NewGuid(), 42, DateTime.Now, 42);
	}
}
