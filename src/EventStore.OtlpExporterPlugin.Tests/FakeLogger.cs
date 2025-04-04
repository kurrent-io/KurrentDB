// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.OtlpExporterPlugin.Tests;

using System.Collections.Generic;
using Serilog;
using Serilog.Events;

public class FakeLogger : ILogger {
	public List<LogEvent> LogMessages { get; } = new();

	public void Write(LogEvent logEvent) {
		LogMessages.Add(logEvent);
	}
}
