// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using Serilog;
using Serilog.Configuration;
using Serilog.Formatting;

namespace EventStore.Common.Log;

internal static class LoggerSinkConfigurationExtensions {
	public static LoggerConfiguration RollingFile(this LoggerSinkConfiguration configuration, string logFileName,
		ITextFormatter expressionTemplate, int retainedFileCountLimit = 31,
		RollingInterval rollingInterval = RollingInterval.Day, int fileSizeLimitBytes = 1024 * 1024 * 1024) {
		if (configuration == null)
			throw new ArgumentNullException(nameof(configuration));

		return configuration.File(
			expressionTemplate,
			logFileName,
			buffered: false,
			rollOnFileSizeLimit: true,
			rollingInterval: rollingInterval,
			retainedFileCountLimit: retainedFileCountLimit,
			fileSizeLimitBytes: fileSizeLimitBytes);
	}
}
