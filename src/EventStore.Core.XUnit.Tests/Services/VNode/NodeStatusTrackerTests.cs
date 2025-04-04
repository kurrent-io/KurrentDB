// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Metrics;
using EventStore.Core.Services.VNode;
using EventStore.Core.XUnit.Tests.Metrics;
using Xunit;

namespace EventStore.Core.TransactionLog.Services.VNode;

public class NodeStatusTrackerTests : IDisposable {
	private readonly TestMeterListener<long> _listener;
	private readonly FakeClock _clock = new();
	private readonly StatusMetric _metric;
	private readonly NodeStatusTracker _sut;

	public NodeStatusTrackerTests() {
		var meter = new Meter($"{typeof(NodeStatusTrackerTests)}");
		_listener = new TestMeterListener<long>(meter);
		_metric = new StatusMetric(
			meter,
			"eventstore-statuses",
			legacyNames: false,
			_clock);
		_sut = new NodeStatusTracker(_metric);
	}

	public void Dispose() {
		_listener?.Dispose();
	}

	[Fact]
	public void can_observe_state_change() {
		_clock.SecondsSinceEpoch = 500;
		AssertMeasurements("Initializing", 500);

		_sut.OnStateChange(Data.VNodeState.PreReplica);
		_clock.SecondsSinceEpoch = 501;
		AssertMeasurements("PreReplica", 501);

		_sut.OnStateChange(Data.VNodeState.Follower);
		_clock.SecondsSinceEpoch = 502;
		AssertMeasurements("Follower", 502);
	}

	[Fact]
	public void can_observe_initial() {
		_clock.SecondsSinceEpoch = 500;
		_sut.OnStateChange(Data.VNodeState.PreLeader);
		_sut.OnStateChange(InaugurationManager.ManagerState.BecomingLeader);
		AssertMeasurements("PreLeader - BecomingLeader", 500);

		_clock.SecondsSinceEpoch = 502;
		_sut.OnStateChange(InaugurationManager.ManagerState.Idle);
		AssertMeasurements("PreLeader", 502);
	}

	[Fact]
	public void can_observe_waiting_for_chaser() {
		_clock.SecondsSinceEpoch = 500;
		_sut.OnStateChange(Data.VNodeState.PreLeader);
		AssertMeasurements("PreLeader", 500);

		_clock.SecondsSinceEpoch = 502;
		_sut.OnStateChange(InaugurationManager.ManagerState.WaitingForChaser);
		AssertMeasurements("PreLeader - WaitingForChaser", 502);
	}

	[Fact]
	public void can_observe_writing_epoch() {
		_clock.SecondsSinceEpoch = 500;
		_sut.OnStateChange(Data.VNodeState.PreLeader);
		AssertMeasurements("PreLeader", 500);

		_clock.SecondsSinceEpoch = 502;
		_sut.OnStateChange(InaugurationManager.ManagerState.WritingEpoch);
		AssertMeasurements("PreLeader - WritingEpoch", 502);
	}

	[Fact]
	public void can_observe_waiting_for_conditions() {
		_clock.SecondsSinceEpoch = 500;
		_sut.OnStateChange(Data.VNodeState.PreLeader);
		AssertMeasurements("PreLeader", 500);

		_clock.SecondsSinceEpoch = 502;
		_sut.OnStateChange(InaugurationManager.ManagerState.WaitingForConditions);
		AssertMeasurements("PreLeader - WaitingForConditions", 502);
	}

	[Fact]
	public void can_observe_becoming_leader() {
		_clock.SecondsSinceEpoch = 500;
		_sut.OnStateChange(Data.VNodeState.PreLeader);
		AssertMeasurements("PreLeader", 500);

		_clock.SecondsSinceEpoch = 502;
		_sut.OnStateChange(InaugurationManager.ManagerState.BecomingLeader);
		AssertMeasurements("PreLeader - BecomingLeader", 502);
	}

	void AssertMeasurements(string expectedStatus, int expectedValue) {
		_listener.Observe();

		Assert.Collection(
			_listener.RetrieveMeasurements("eventstore-statuses"),
			m => {
				Assert.Equal(expectedValue, m.Value);
				Assert.Collection(
					m.Tags.ToArray(),
					t => {
						Assert.Equal("name", t.Key);
						Assert.Equal("Node", t.Value);
					},
					t => {
						Assert.Equal("status", t.Key);
						Assert.Equal(expectedStatus, t.Value);
					});
			});
	}
}
