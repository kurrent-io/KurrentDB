// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using EventStore.Common.Utils;
using EventStore.Core.Messages;

namespace EventStore.Core.Metrics;

public class PersistentSubscriptionTracker : IPersistentSubscriptionTracker {
	private IReadOnlyList<MonitoringMessage.PersistentSubscriptionInfo> _currentStats = [];

	public void OnNewStats(IReadOnlyList<MonitoringMessage.PersistentSubscriptionInfo> newStats) {
		_currentStats = newStats ?? [];
	}

	public IEnumerable<Measurement<long>> ObserveConnectionsCount() =>
		_currentStats.Select(x =>
			new Measurement<long>(x.Connections.Count, [
				new("event_stream_id", x.EventSource),
				new("group_name", x.GroupName)]));

	public IEnumerable<Measurement<long>> ObserveParkedMessages() =>
		_currentStats.Select(x =>
			new Measurement<long>(x.ParkedMessageCount, [
				new("event_stream_id", x.EventSource),
				new("group_name", x.GroupName)
			]));

	public IEnumerable<Measurement<long>> ObserveInFlightMessages() =>
		_currentStats.Select(x =>
			new Measurement<long>(x.TotalInFlightMessages, [
				new("event_stream_id", x.EventSource),
				new("group_name", x.GroupName)
			]));

	public IEnumerable<Measurement<long>> ObserveOldestParkedMessage() =>
		_currentStats.Select(x =>
			new Measurement<long>(x.OldestParkedMessage, [
				new("event_stream_id", x.EventSource),
				new("group_name", x.GroupName)
			]));

	public IEnumerable<Measurement<long>> ObserveItemsProcessed() =>
		_currentStats.Select(x =>
			new Measurement<long>(x.TotalItems, [
				new("event_stream_id", x.EventSource),
				new("group_name", x.GroupName)
			]));

	public IEnumerable<Measurement<long>> ObserveLastKnownEvent() =>
		_currentStats
			.Where(x => x.EventSource != "$all")
			.Select(x => {
				var measurement = long.TryParse(x.LastKnownEventPosition, out var lastEventPos)
					? lastEventPos
					: 0;
				return new Measurement<long>(measurement, [
					new("event_stream_id", x.EventSource),
					new("group_name", x.GroupName)
				]);
			});

	public IEnumerable<Measurement<long>> ObserveLastKnownEventCommitPosition() =>
		_currentStats
			.Where(x => x.EventSource == "$all")
			.Select(x => {
				var (eventCommitPosition, _) = EventPositionParser.ParseCommitPreparePosition(x.LastKnownEventPosition);
				return new Measurement<long>(eventCommitPosition, [
					new("event_stream_id", x.EventSource),
					new("group_name", x.GroupName)
				]);
			});

	public IEnumerable<Measurement<long>> ObserveLastCheckpointedEvent() =>
		_currentStats
			.Where(x => x.EventSource != "$all")
			.Select(x => {
				var measurement = long.TryParse(x.LastCheckpointedEventPosition, out var lastEventPos)
					? lastEventPos
					: 0;
				return new Measurement<long>(measurement, [
					new("event_stream_id", x.EventSource),
					new("group_name", x.GroupName)
				]);
			});

	public IEnumerable<Measurement<long>> ObserveLastCheckpointedEventCommitPosition() =>
		_currentStats
			.Where(x => x.EventSource == "$all")
			.Select(statistics => {
				var (checkpointedCommitPosition, _) = EventPositionParser.ParseCommitPreparePosition(statistics.LastCheckpointedEventPosition);
				return new Measurement<long>(checkpointedCommitPosition, [
					new("event_stream_id", statistics.EventSource),
					new("group_name", statistics.GroupName)
				]);
			});
}
