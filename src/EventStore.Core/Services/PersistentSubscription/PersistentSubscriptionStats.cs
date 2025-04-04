// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EventStore.Core.Messages;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionStats {
	private long _totalItems;
	private TimeSpan _lastTotalTime;
	private long _lastTotalItems;
	private IPersistentSubscriptionStreamPosition _lastCheckpointedEventPosition;
	private IPersistentSubscriptionStreamPosition _lastKnownEventPosition;
	private readonly PersistentSubscription _parent;
	private readonly Stopwatch _totalTimeWatch;
	private readonly PersistentSubscriptionParams _settings;

	public PersistentSubscriptionStats(PersistentSubscription parent, PersistentSubscriptionParams settings,
		Stopwatch totalTimeWatch) {
		_settings = settings;
		_parent = parent;
		_totalTimeWatch = totalTimeWatch;
	}

	public void IncrementProcessed() {
		Interlocked.Increment(ref _totalItems);
	}

	public void SetLastCheckPoint(IPersistentSubscriptionStreamPosition lastCheckpointedEventPosition) {
		_lastCheckpointedEventPosition = lastCheckpointedEventPosition;
	}

	public void SetLastKnownEventPosition(IPersistentSubscriptionStreamPosition knownEventPosition) {
		if (knownEventPosition == null)
			return;
		if (_lastKnownEventPosition == null || _lastKnownEventPosition.CompareTo(knownEventPosition) < 0)
			_lastKnownEventPosition = knownEventPosition;
	}

	public MonitoringMessage.PersistentSubscriptionInfo GetStatistics() {
		long oldestParkedMessage = 0;
		var totalTime = _totalTimeWatch.Elapsed;
		var totalItems = Interlocked.Read(ref _totalItems);

		var lastRunMs = totalTime - _lastTotalTime;
		var lastItems = totalItems - _lastTotalItems;

		var avgItemsPerSecond =
			lastRunMs.Ticks != 0 ? (int)(TimeSpan.TicksPerSecond * lastItems / lastRunMs.Ticks) : 0;
		_lastTotalTime = totalTime;
		_lastTotalItems = totalItems;
		var connections = new List<MonitoringMessage.ConnectionInfo>();
		var totalInflight = 0;
		foreach (var conn in _parent._pushClients.GetAll()) {
			var connItems = conn.TotalItems;
			var connLastItems = connItems - conn.LastTotalItems;
			conn.LastTotalItems = connItems;
			var connAvgItemsPerSecond = lastRunMs.Ticks != 0
				? (int)(TimeSpan.TicksPerSecond * connLastItems / lastRunMs.Ticks)
				: 0;
			var extraStats = conn.GetExtraStats();
			var stats = extraStats == null ? new List<Measurement>() : extraStats.Measurements;
			totalInflight += conn.InflightMessages;
			connections.Add(new MonitoringMessage.ConnectionInfo {
				From = conn.From,
				Username = conn.Username,
				AverageItemsPerSecond = connAvgItemsPerSecond,
				TotalItems = conn.TotalItems,
				InFlightMessages = conn.InflightMessages,
				AvailableSlots = conn.AvailableSlots,
				CountSinceLastMeasurement = connLastItems,
				ObservedMeasurements = stats,
				ConnectionName = conn.ConnectionName,
			});
		}

		long parkedMessageCount = _settings.MessageParker.ParkedMessageCount;

		var timeStamp = _settings.MessageParker.GetOldestParkedMessage;

		if (timeStamp.HasValue) {
			oldestParkedMessage = (long)(DateTime.UtcNow - timeStamp.Value).TotalSeconds;
		}

		var gotBuffer = _parent.TryGetStreamBuffer(out var streamBuffer);

		return new MonitoringMessage.PersistentSubscriptionInfo() {
			EventSource = _parent.EventSource.ToString(),
			GroupName = _parent.GroupName,
			Status = _parent.State.ToString(),
			Connections = connections,
			AveragePerSecond = avgItemsPerSecond,
			LastCheckpointedEventPosition = _lastCheckpointedEventPosition?.ToString(),
			LastKnownEventPosition = _lastKnownEventPosition?.ToString(),
			TotalItems = totalItems,
			CountSinceLastMeasurement = lastItems,
			CheckPointAfterMilliseconds = (int)_settings.CheckPointAfter.TotalMilliseconds,
			BufferSize = _settings.BufferSize,
			LiveBufferSize = _settings.LiveBufferSize,
			MaxCheckPointCount = _settings.MaxCheckPointCount,
			MaxRetryCount = _settings.MaxRetryCount,
			MessageTimeoutMilliseconds = (int)_settings.MessageTimeout.TotalMilliseconds,
			MinCheckPointCount = _settings.MinCheckPointCount,
			ReadBatchSize = _settings.ReadBatchSize,
			ResolveLinktos = _settings.ResolveLinkTos,
			StartFrom = _settings.StartFrom?.ToString(),
			ReadBufferCount = gotBuffer ? streamBuffer.ReadBufferCount : 0,
			RetryBufferCount = gotBuffer ? streamBuffer.RetryBufferCount : 0,
			LiveBufferCount = gotBuffer ? streamBuffer.LiveBufferCount : 0,
			ExtraStatistics = _settings.ExtraStatistics,
			TotalInFlightMessages = totalInflight,
			OutstandingMessagesCount = _parent.OutstandingMessageCount,
			NamedConsumerStrategy = _settings.ConsumerStrategy.Name,
			MaxSubscriberCount = _settings.MaxSubscriberCount,
			ParkedMessageCount = parkedMessageCount,
			OldestParkedMessage = oldestParkedMessage
		};
	}
}
