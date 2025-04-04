using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.Chunks;

public class TFChunkTrackerTests : IDisposable {
	private readonly TFChunkTracker _sut;
	private readonly TestMeterListener<long> _listener;

	public TFChunkTrackerTests() {
		var meter = new Meter($"{typeof(TFChunkTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		var byteMetric = new CounterMetric(meter, "eventstore-io", unit: "bytes");
		var eventMetric = new CounterMetric(meter, "eventstore-io", unit: "events");

		var readTag = new KeyValuePair<string, object>("activity", "read");
		_sut = new TFChunkTracker(
			readBytes: new CounterSubMetric(byteMetric, new[] { readTag }),
			readEvents: new CounterSubMetric(eventMetric, new[] { readTag }));
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void can_observe_prepare_log() {
		var prepare = CreatePrepare(
			data: new byte[5],
			meta: new byte[5]);

		_sut.OnRead(prepare);
		_listener.Observe();

		AssertEventsRead(1);
		AssertBytesRead(10);
	}

	[Fact]
	public void disregard_system_log() {
		var system = CreateSystemRecord();
		_sut.OnRead(system);
		_listener.Observe();

		AssertEventsRead(0);
		AssertBytesRead(0);
	}

	[Fact]
	public void disregard_commit_log() {
		var system = CreateCommit();
		_sut.OnRead(system);
		_listener.Observe();

		AssertEventsRead(0);
		AssertBytesRead(0);
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

	private static PrepareLogRecord CreatePrepare(byte[] data, byte[] meta) {
		return new PrepareLogRecord(42, Guid.NewGuid(), Guid.NewGuid(), 42, 42, "tests", null, 42, DateTime.Now,
			PrepareFlags.Data, "type-test", null, data, meta);
	}

	private static SystemLogRecord CreateSystemRecord() {
		return new SystemLogRecord(42, DateTime.Now, SystemRecordType.Epoch, SystemRecordSerialization.Binary, Array.Empty<byte>());
	}

	private static CommitLogRecord CreateCommit() {
		return new CommitLogRecord(42, Guid.NewGuid(), 42, DateTime.Now, 42);
	}
}
