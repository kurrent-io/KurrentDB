// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.Tracing;

namespace EventStore.Core.Metrics;

public class GcSuspensionMetric : EventListener {
	private const int GcKeyword = 0x0000001;
	private const int GCSuspendEEBegin = 9;
	private const int GCRestartEEEnd = 3;
	private const uint SuspendForGc = 0x1;
	private const uint SuspendForGcPrep = 0x6;
	private readonly DurationMaxTracker _tracker;
	private DateTime? _started;

	public GcSuspensionMetric(DurationMaxTracker tracker) {
		_tracker = tracker;
	}

	protected override void OnEventSourceCreated(EventSource eventSource) {
		if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime")) {
			EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)GcKeyword);
		}
	}

	protected override void OnEventWritten(EventWrittenEventArgs eventData) {
		if (_tracker == null)
			return;

		switch (eventData.EventId) {
			case GCSuspendEEBegin: {
				var idx = eventData.PayloadNames!.IndexOf("Reason");
				var value = (uint)eventData.Payload![idx]!;

				// We only track suspensions that are meant for garbage collection.
				// See https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-garbage-collection-events
				if (value is SuspendForGc or SuspendForGcPrep)
					_started = eventData.TimeStamp;

				break;
			}

			case GCRestartEEEnd: {
				// Means that the suspension end event comes from a suspension that was not due to garbage collection.
				if (!_started.HasValue)
					return;

				_tracker.RecordNow(eventData.TimeStamp.Subtract(_started.Value));
				_started = null;
				break;
			}
		}
	}
}
