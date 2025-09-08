// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using Serilog.Events;

// ReSharper disable once CheckNamespace

namespace KurrentDB.Common.Log;

public class SerilogEventListener : EventListener {
	private readonly Dictionary<string, LogEventLevel> _eventSources = new() {
		{ "kurrentdb-dev-certs", LogEventLevel.Verbose }
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
		return level switch {
			EventLevel.Critical => LogEventLevel.Fatal,
			EventLevel.Error => LogEventLevel.Error,
			EventLevel.Informational => LogEventLevel.Information,
			EventLevel.Verbose => LogEventLevel.Verbose,
			EventLevel.Warning => LogEventLevel.Warning,
			_ => LogEventLevel.Information
		};
	}
	private static EventLevel ConvertToEventSourceLevel(LogEventLevel level) {
		return level switch {
			LogEventLevel.Fatal => EventLevel.Critical,
			LogEventLevel.Error => EventLevel.Error,
			LogEventLevel.Information => EventLevel.Informational,
			LogEventLevel.Verbose => EventLevel.Verbose,
			LogEventLevel.Warning => EventLevel.Warning,
			_ => EventLevel.Informational
		};
	}
}
