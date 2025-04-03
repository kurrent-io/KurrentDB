// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Index;
using EventStore.Core.Metrics;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Index;

public class IndexTrackerTests : IDisposable {
	private readonly TestMeterListener<long> _listener;
	private readonly IndexTracker _sut;

	public IndexTrackerTests() {
		var meter = new Meter($"{typeof(IndexTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);

		var eventMetric = new CounterMetric(meter, "eventstore-io", "events", legacyNames: false);
		_sut = new IndexTracker(new CounterSubMetric(eventMetric, new[] {new KeyValuePair<string, object>("activity", "written")}));
	}

	public void Dispose() {
		_listener.Dispose();
	}

	[Fact]
	public void can_observe_prepare_logs() {
		var prepares = new List<IPrepareLogRecord<string>> {
			CreatePrepare(),
			CreatePrepare(),
			CreatePrepare()
		};

		_sut.OnIndexed(prepares);
		_listener.Observe();

		AssertMeasurements(expectedEventsWritten: 3);
	}

	private static PrepareLogRecord CreatePrepare() {
		return new PrepareLogRecord(42, Guid.NewGuid(), Guid.NewGuid(), 42, 42, "tests", null, 42, DateTime.Now,
			PrepareFlags.Data, "type-test", null, Array.Empty<byte>(), Array.Empty<byte>());
	}

	private void AssertMeasurements(long expectedEventsWritten) {
		Assert.Collection(
			_listener.RetrieveMeasurements("eventstore-io-events"),
			m => {
				Assert.Equal(expectedEventsWritten, m.Value);
				Assert.Collection(m.Tags.ToArray(), t => {
					Assert.Equal("activity", t.Key);
					Assert.Equal("written", t.Value);
				});
			});
	}
}
