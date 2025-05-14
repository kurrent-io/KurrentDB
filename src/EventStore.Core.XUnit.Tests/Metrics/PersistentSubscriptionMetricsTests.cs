// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Metrics;
using EventStore.Core.Services.PersistentSubscription;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Metrics;

public class PersistentSubscriptionMetricsTests {
	readonly PersistentSubscriptionTracker _sut = new();

	public PersistentSubscriptionMetricsTests() {
		var statsSampleOne = new MonitoringMessage.PersistentSubscriptionInfo() {
			EventSource = "test",
			GroupName = "testGroup",
			AveragePerSecond = 1,
			BufferSize = 500,
			CheckPointAfterMilliseconds = 1000,
			Connections = Enumerable.Range(0, 1001).Select(x => new MonitoringMessage.ConnectionInfo()).ToList(),
			CountSinceLastMeasurement = 5,
			ExtraStatistics = false,
			LastCheckpointedEventPosition = "1013",
			LastKnownEventPosition = "1011",
			LiveBufferCount = 0,
			LiveBufferSize = 500,
			MaxCheckPointCount = 500,
			MaxRetryCount = 5,
			MaxSubscriberCount = 10,
			MessageTimeoutMilliseconds = 1000,
			MinCheckPointCount = 10,
			NamedConsumerStrategy = "Round Robin",
			OldestParkedMessage = 1007,
			OutstandingMessagesCount = 2,
			ParkedDueToClientNak = 2001,
			ParkedDueToMaxRetries = 2002,
			ParkedMessageReplays = 2003,
			ParkedMessageCount = 1003,
			ReadBatchSize = 20,
			ReadBufferCount = 0,
			ResolveLinktos = true,
			RetryBufferCount = 0,
			StartFrom = 0.ToString(),
			Status = PersistentSubscriptionState.Live.ToString(),
			TotalInFlightMessages = 1005,
			TotalItems = 1009,
		};

		var statsSampleTwo = new MonitoringMessage.PersistentSubscriptionInfo() {
			EventSource = "$all",
			GroupName = "testGroup",
			AveragePerSecond = 1,
			BufferSize = 500,
			CheckPointAfterMilliseconds = 1000,
			Connections = Enumerable.Range(0, 1002).Select(x => new MonitoringMessage.ConnectionInfo()).ToList(),
			CountSinceLastMeasurement = 5,
			ExtraStatistics = false,
			LastCheckpointedEventPosition = "C:1014/P:5",
			LastKnownEventPosition = "C:1012/P:10",
			LiveBufferCount = 0,
			LiveBufferSize = 500,
			MaxCheckPointCount = 500,
			MaxRetryCount = 5,
			MaxSubscriberCount = 10,
			MessageTimeoutMilliseconds = 1000,
			MinCheckPointCount = 10,
			NamedConsumerStrategy = "Round Robin",
			OldestParkedMessage = 1008,
			OutstandingMessagesCount = 2,
			ParkedDueToClientNak = 2004,
			ParkedDueToMaxRetries = 2005,
			ParkedMessageReplays = 2006,
			ParkedMessageCount = 1004,
			ReadBatchSize = 20,
			ReadBufferCount = 0,
			ResolveLinktos = true,
			RetryBufferCount = 0,
			StartFrom = 0.ToString(),
			Status = PersistentSubscriptionState.Live.ToString(),
			TotalInFlightMessages = 1006,
			TotalItems = 1010,
		};

		_sut.OnNewStats([
			statsSampleOne,
			statsSampleTwo,
		]);
	}

	[Fact]
	public void ObserveConnectionsCount() {
		var measurements = _sut.ObserveConnectionsCount();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1001),
			AssertMeasurement("$all", "testGroup", 1002));
	}

	[Fact]
	public void ObserveParkedMessages() {
		var measurements = _sut.ObserveParkedMessages();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1003),
			AssertMeasurement("$all", "testGroup", 1004));
	}

	[Fact]
	public void ObserveParkMessageRequests() {
		var measurements = _sut.ObserveParkMessageRequests();
		Assert.Collection(measurements,
			actual => {
				Assert.Equal(2001, actual.Value);
				Assert.Collection(
					actual.Tags.ToArray(),
					AssertTag("event_stream_id", "test"),
					AssertTag("group_name", "testGroup"),
					AssertTag("reason", "client-nak"));
			},
			actual => {
				Assert.Equal(2002, actual.Value);
				Assert.Collection(
					actual.Tags.ToArray(),
					AssertTag("event_stream_id", "test"),
					AssertTag("group_name", "testGroup"),
					AssertTag("reason", "max-retries"));
			},
			actual => {
				Assert.Equal(2004, actual.Value);
				Assert.Collection(
					actual.Tags.ToArray(),
					AssertTag("event_stream_id", "$all"),
					AssertTag("group_name", "testGroup"),
					AssertTag("reason", "client-nak"));
			},
			actual => {
				Assert.Equal(2005, actual.Value);
				Assert.Collection(
					actual.Tags.ToArray(),
					AssertTag("event_stream_id", "$all"),
					AssertTag("group_name", "testGroup"),
					AssertTag("reason", "max-retries"));
			});
	}

	[Fact]
	public void ObserveParkedMessageReplays() {
		var measurements = _sut.ObserveParkedMessageReplays();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 2003),
			AssertMeasurement("$all", "testGroup", 2006));
	}

	[Fact]
	public void ObserveInFlightMessages() {
		var measurements = _sut.ObserveInFlightMessages();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1005),
			AssertMeasurement("$all", "testGroup", 1006));
	}

	[Fact]
	public void ObserveOldestParkedMessage() {
		var measurements = _sut.ObserveOldestParkedMessage();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1007),
			AssertMeasurement("$all", "testGroup", 1008));
	}

	[Fact]
	public void ObserveItemsProcessed() {
		var measurements = _sut.ObserveItemsProcessed();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1009),
			AssertMeasurement("$all", "testGroup", 1010));
	}

	[Fact]
	public void ObserveLastKnownEvent() {
		var measurements = _sut.ObserveLastKnownEvent();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1011));
	}

	[Fact]
	public void ObserveLastKnownEventCommitPosition() {
		var measurements = _sut.ObserveLastKnownEventCommitPosition();
		Assert.Collection(measurements,
			AssertMeasurement("$all", "testGroup", 1012));
	}

	[Fact]
	public void ObserveLastCheckpointedEvent() {
		var measurements = _sut.ObserveLastCheckpointedEvent();
		Assert.Collection(measurements,
			AssertMeasurement("test", "testGroup", 1013));
	}

	[Fact]
	public void ObserveLastCheckpointedEventCommitPosition() {
		var measurements = _sut.ObserveLastCheckpointedEventCommitPosition();
		Assert.Collection(measurements,
			AssertMeasurement("$all", "testGroup", 1014));
	}

	static Action<Measurement<long>> AssertMeasurement(
		string sourceName,
		string groupName,
		long expectedValue) =>

		actualMeasurement => {
			Assert.Equal(expectedValue, actualMeasurement.Value);
			Assert.Collection(
				actualMeasurement.Tags.ToArray(),
				AssertTag("event_stream_id", sourceName),
				AssertTag("group_name", groupName));
		};

	static Action<KeyValuePair<string, object>> AssertTag(string key, object value) =>
		actualTag => {
			Assert.Equal(key, actualTag.Key);
			Assert.Equal(value, actualTag.Value);
		};
}
