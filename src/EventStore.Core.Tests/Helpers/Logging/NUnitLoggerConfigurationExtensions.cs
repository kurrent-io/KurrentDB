// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace EventStore.Core.Tests.Helpers.Logging;

public static class NUnitLoggerConfigurationExtensions {
	const string DefaultOutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}  {Exception}";

	public static LoggerConfiguration NUnitOutput(
		this LoggerSinkConfiguration sinkConfiguration,
		LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
		IFormatProvider formatProvider = null,
		LoggingLevelSwitch levelSwitch = null,
		string outputTemplate = DefaultOutputTemplate) {

		ArgumentNullException.ThrowIfNull(sinkConfiguration);
		var formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);
		return sinkConfiguration.Sink(new NUnitSink(formatter), restrictedToMinimumLevel, levelSwitch);
	}
}
