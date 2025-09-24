// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace KurrentDB.Core.Tests.Logging;

public static class TestOutputLoggerExtensions {
	internal const string DefaultConsoleOutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

	public static LoggerConfiguration TestOutput(
		this LoggerSinkConfiguration sinkConfiguration,
		LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
		LoggingLevelSwitch levelSwitch = null)
	{
		ArgumentNullException.ThrowIfNull(sinkConfiguration);
		var formatter = new MessageTemplateTextFormatter(DefaultConsoleOutputTemplate);
		return sinkConfiguration.Sink(new TestOutputSink(formatter), restrictedToMinimumLevel, levelSwitch);
	}
}
