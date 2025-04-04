// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Serilog.Events;

namespace EventStore.Common.Log;

public class SerilogEventListener : EventListener {
	private readonly Dictionary<string, LogEventLevel> _eventSources = new() {
		{ "eventstore-dev-certs", LogEventLevel.Verbose }
	};

	protected override void OnEventSourceCreated(EventSource eventSource) {
		if (_eventSources.TryGetValue(eventSource.Name, out var level)) {
			EnableEvents(eventSource, ConvertToEventSourceLevel(level));
		}
	}

	protected override void OnEventWritten(EventWrittenEventArgs eventData) {
		if (eventData.Message is null)
			return;
		Serilog.Log.Logger.Write(ConvertToSerilogLevel(eventData.Level), eventData.Message, eventData.Payload?.ToArray());
	}

	private static LogEventLevel ConvertToSerilogLevel(EventLevel level) {
		switch (level) {
			case EventLevel.Critical:
				return LogEventLevel.Fatal;
			case EventLevel.Error:
				return LogEventLevel.Error;
			case EventLevel.Informational:
				return LogEventLevel.Information;
			case EventLevel.Verbose:
				return LogEventLevel.Verbose;
			case EventLevel.Warning:
				return LogEventLevel.Warning;
			case EventLevel.LogAlways:
				return LogEventLevel.Information;
		}

		return LogEventLevel.Information;
	}
	private static EventLevel ConvertToEventSourceLevel(LogEventLevel level) {
		switch (level) {
			case LogEventLevel.Fatal:
				return EventLevel.Critical;
			case LogEventLevel.Error:
				return EventLevel.Error;
			case LogEventLevel.Information:
				return EventLevel.Informational;
			case LogEventLevel.Verbose:
				return EventLevel.Verbose;
			case LogEventLevel.Warning:
				return EventLevel.Warning;
		}

		return EventLevel.Informational;
	}
}
