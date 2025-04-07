// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Tests;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class ElectionsCounterTrackerTests : IDisposable {
	private readonly Disposables _disposables = new();
	private readonly ElectionMessage.ElectionsDone _electionsDoneMessage;

	public ElectionsCounterTrackerTests() {
		var endPoint = new DnsEndPoint("127.0.0.1", 1113);
		var memberInfo = Cluster.MemberInfo.Initial(Guid.Empty, DateTime.UtcNow,
			VNodeState.Unknown, true,
			endPoint, endPoint, endPoint, endPoint, endPoint,
			null, 0, 0, 0, false);
		_electionsDoneMessage = new ElectionMessage.ElectionsDone(1, 1, memberInfo);
	}

	public void Dispose() {
		_disposables?.Dispose();
	}

	private (ElectionsCounterTracker, TestMeterListener<long>) GenSut(
		[CallerMemberName] string callerName = "") {

		var meter = new Meter($"{typeof(ElectionsCounterTrackerTests)}--{callerName}").DisposeWith(_disposables);
		var listener = new TestMeterListener<long>(meter).DisposeWith(_disposables);
		var metric = new CounterMetric(meter, "test-metric", unit: "");
		var sut = new ElectionsCounterTracker(new CounterSubMetric(metric, []));

		return (sut, listener);
	}

	[Fact]
	public void test_election_count_for_one_election() {
		var (sut, listener) = GenSut();
		sut.Handle(_electionsDoneMessage);

		AssertMeasurements(listener, AssertMeasurement(1));
	}

	[Fact]
	public void test_election_count_for_five_elections() {
		var (sut, listener) = GenSut();
		sut.Handle(_electionsDoneMessage);
		sut.Handle(_electionsDoneMessage);
		sut.Handle(_electionsDoneMessage);
		sut.Handle(_electionsDoneMessage);
		sut.Handle(_electionsDoneMessage);

		AssertMeasurements(listener, AssertMeasurement(5));
	}

	static Action<TestMeterListener<long>.TestMeasurement> AssertMeasurement(
		int expectedValue) =>
		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
		};

	static void AssertMeasurements(
		TestMeterListener<long> listener,
		params Action<TestMeterListener<long>.TestMeasurement>[] actions) {

		listener.Observe();
		Assert.Collection(listener.RetrieveMeasurements("test-metric"), actions);
	}
}
