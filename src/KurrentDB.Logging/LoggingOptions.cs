// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Common.Options;
using KurrentDB.Common.Utils;
using Serilog;

namespace KurrentDB.Common.Log;

[Description("Logging Options")]
public record LoggingOptions {
	[Description("Path where to keep log files.")]
	public string Log { get; init; } = Locations.DefaultLogDirectory;

	[Description("The name of the log configuration file.")]
	public string LogConfig { get; init; } = "logconfig.json";

	[Description("Sets the minimum log level. For more granular settings, please edit logconfig.json.")]
	public LogLevel LogLevel { get; init; } = LogLevel.Default;

	[Description("Which format (plain, json) to use when writing to the console.")]
	public LogConsoleFormat LogConsoleFormat { get; init; } = LogConsoleFormat.Plain;

	[Description("Maximum size of each log file.")]
	public int LogFileSize { get; init; } = 1024 * 1024 * 1024;

	[Description("How often to rotate logs.")]
	public RollingInterval LogFileInterval { get; init; } = RollingInterval.Day;

	[Description("How many log files to hold on to.")]
	public int LogFileRetentionCount { get; init; } = 31;

	[Description("Disable log to disk.")]
	public bool DisableLogFile { get; init; } = false;
}

