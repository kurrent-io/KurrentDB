// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using KurrentDB.Common.Configuration;
using KurrentDB.Common.Options;
using KurrentDB.Common.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace KurrentDB.Common.Log;

[Description("Logging Options")]
public record LoggingOptions : IConfigurationBinder<LoggingOptions> {
	[Description("Path where to keep log files.")]
	public string Log { get; set; } = Locations.DefaultLogDirectory;

	[Description("The name of the log configuration file.")]
	public string LogConfig { get; set; } = "logconfig.json";

	[Description("Sets the minimum log level. For more granular settings, please edit logconfig.json.")]
	public LogLevel LogLevel { get; set; } = LogLevel.Default;

	[Description("Which format (plain, json) to use when writing to the console.")]
	public LogConsoleFormat LogConsoleFormat { get; set; } = LogConsoleFormat.Plain;

	[Description("Maximum size of each log file.")]
	public int LogFileSize { get; set; } = 1024 * 1024 * 1024;

	[Description("How often to rotate logs.")]
	public RollingInterval LogFileInterval { get; set; } = RollingInterval.Day;

	[Description("How many log files to hold on to.")]
	public int LogFileRetentionCount { get; set; } = 31;

	[Description("Disable log to disk.")]
	public bool DisableLogFile { get; set; } = false;

	static LoggingOptions IConfigurationBinder<LoggingOptions>.Bind(IConfiguration configuration)
		=> configuration.Get<LoggingOptions>()!;
}

