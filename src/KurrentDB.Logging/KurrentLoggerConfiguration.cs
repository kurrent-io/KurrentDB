// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Options;
using KurrentDB.Core.Configuration;
using KurrentDB.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Templates;
using Serilog.Templates.Themes;

// Resharper disable CheckNamespace

namespace KurrentDB.Common.Log;

public class KurrentLoggerConfiguration {
	static readonly ExpressionTemplate ConsoleOutputExpressionTemplate = new(
		"[{ProcessId,5},{ThreadId,2},{@t:HH:mm:ss.fff},{@l:u3}] {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1), -30} {@m}\n{@x}",
		theme: TemplateTheme.Literate
	);

	private const string CompactJsonTemplate = "{ {@t, @mt, @r, @l, @i, @x, ..@p} }\n";

	public static readonly Logger ConsoleLog = StandardLoggerConfiguration
		.WriteTo.Console(ConsoleOutputExpressionTemplate)
		.CreateLogger();

	private static readonly Func<LogEvent, bool> RegularStats = Matching.FromSource("REGULAR-STATS-LOGGER");

	private static LoggingLevelSwitch DefaultLogLevelSwitch = new() { MinimumLevel = LogEventLevel.Verbose };
	private static readonly object DefaultLogLevelSwitchLock = new();

	private readonly string _logsDirectory;
	private readonly string _componentName;
	private readonly LoggerConfiguration _loggerConfiguration;

	private static readonly ExpressionTemplate JsonTemplate = new(CompactJsonTemplate);

	// ReSharper disable once NotAccessedField.Local
	private static readonly SerilogEventListener SerilogEventListener;

	static KurrentLoggerConfiguration() {
		Serilog.Log.Logger = ConsoleLog;
		AppDomain.CurrentDomain.UnhandledException += (s, e) => {
			if (e.ExceptionObject is Exception exc)
				Serilog.Log.Fatal(exc, "Global Unhandled Exception occurred.");
			else
				Serilog.Log.Fatal("Global Unhandled Exception object: {e}.", e.ExceptionObject);
		};
		SerilogEventListener = new();
	}

	public static LoggerConfiguration CreateLoggerConfiguration(IConfiguration configuration, LoggingOptions options, string componentName) {
		if (options.Log.StartsWith('~')) {
			throw new ApplicationInitializationException("The given log path starts with a '~'. KurrentDB does not expand '~'.");
		}

		var configurationRoot = new ConfigurationBuilder()
			.AddKurrentConfigFile(options.LogConfig, reloadOnChange: true)
			.Build();

		SelfLog.Enable(ConsoleLog.Information);

		var logConfig = configurationRoot.GetSection("Serilog").Exists()
			? new LoggerConfiguration()
				.Enrich.WithProperty(Constants.SourceContextPropertyName, "KurrentDB")
				.ReadFrom.Configuration(configurationRoot)
			: Default(options.Log, componentName, configurationRoot, options.LogConsoleFormat, options.LogFileInterval,
				options.LogFileSize, options.LogFileRetentionCount, options.DisableLogFile);

		var logExportEnabled = configuration.GetValue<bool>("KurrentDB:OpenTelemetry:Logging:Enabled");
		if (logExportEnabled) {
			logConfig.WriteTo.Sink(new OpenTelemetryLogger(configuration));
		}
		SelfLog.Disable();
		return logConfig;
	}

	public static bool AdjustMinimumLogLevel(LogLevel logLevel) {
		lock (DefaultLogLevelSwitchLock) {
#if !DEBUG
			if (DefaultLogLevelSwitch == null) {
				throw new InvalidOperationException("The logger configuration has not yet been initialized.");
			}
#endif
			if (!Enum.TryParse<LogEventLevel>(logLevel.ToString(), out var serilogLogLevel)) {
				throw new ArgumentException($"'{logLevel}' is not a valid log level.");
			}

			if (serilogLogLevel == DefaultLogLevelSwitch.MinimumLevel)
				return false;
			DefaultLogLevelSwitch.MinimumLevel = serilogLogLevel;
			return true;
		}
	}

	private static LoggerConfiguration Default(string logsDirectory, string componentName,
		IConfigurationRoot logLevelConfigurationRoot, LogConsoleFormat logConsoleFormat,
		RollingInterval logFileInterval, int logFileSize, int logFileRetentionCount, bool disableLogFile) =>
		new KurrentLoggerConfiguration(logsDirectory, componentName, logLevelConfigurationRoot, logConsoleFormat,
			logFileInterval, logFileSize, logFileRetentionCount, disableLogFile);

	private KurrentLoggerConfiguration(string logsDirectory, string componentName,
		IConfigurationRoot logLevelConfigurationRoot, LogConsoleFormat logConsoleFormat,
		RollingInterval logFileInterval, int logFileSize, int logFileRetentionCount, bool disableLogFile) {
		ArgumentNullException.ThrowIfNull(logsDirectory);
		ArgumentNullException.ThrowIfNull(componentName);
		ArgumentNullException.ThrowIfNull(logLevelConfigurationRoot);

		_logsDirectory = logsDirectory;
		_componentName = componentName;

		var loglevelSection = logLevelConfigurationRoot.GetSection("Logging").GetSection("LogLevel");
		var defaultLogLevelSection = loglevelSection.GetSection("Default");
		lock (DefaultLogLevelSwitchLock) {
			DefaultLogLevelSwitch = new LoggingLevelSwitch {
				MinimumLevel = LogEventLevel.Verbose
			};
			ApplyLogLevel(defaultLogLevelSection, DefaultLogLevelSwitch);
		}

		var loggerConfiguration = StandardLoggerConfiguration
			.MinimumLevel.ControlledBy(DefaultLogLevelSwitch)
			.WriteTo.Async(AsyncSink);

		foreach (var namedLogLevelSection in loglevelSection.GetChildren().Where(x => x.Key != "Default")) {
			var levelSwitch = new LoggingLevelSwitch();
			ApplyLogLevel(namedLogLevelSection, levelSwitch);
			loggerConfiguration = loggerConfiguration.MinimumLevel.Override(namedLogLevelSection.Key, levelSwitch);
		}

		_loggerConfiguration = loggerConfiguration;

		void AsyncSink(LoggerSinkConfiguration configuration) {
			configuration.Logger(c => c
				.Filter.ByIncludingOnly(RegularStats)
				.WriteTo.Logger(Stats));
			configuration.Logger(c => c
				.Filter.ByExcluding(RegularStats)
				.WriteTo.Logger(Default));
		}

		void Default(LoggerConfiguration configuration) {
			configuration.WriteTo.Console(
				logConsoleFormat == LogConsoleFormat.Plain
					? ConsoleOutputExpressionTemplate
					: JsonTemplate);

			if (!disableLogFile) {
				configuration.WriteTo
					.RollingFile(GetLogFileName(), JsonTemplate, logFileRetentionCount, logFileInterval, logFileSize)
					.WriteTo.Logger(Error);
			}

			configuration.WriteTo.Sink(ObservableSerilogSink.Instance);
		}

		void Error(LoggerConfiguration configuration) {
			if (!disableLogFile) {
				configuration
					.Filter.ByIncludingOnly(Errors)
					.WriteTo
					.RollingFile(GetLogFileName("err"), JsonTemplate, logFileRetentionCount, logFileInterval, logFileSize);
			}
		}

		void Stats(LoggerConfiguration configuration) {
			if (!disableLogFile) {
				configuration.WriteTo.RollingFile(GetLogFileName("stats"), JsonTemplate, logFileRetentionCount, logFileInterval, logFileSize);
			}
		}

		void ApplyLogLevel(IConfigurationSection namedLogLevelSection, LoggingLevelSwitch levelSwitch) {
			TrySetLogLevel(namedLogLevelSection, levelSwitch);
			ChangeToken.OnChange(namedLogLevelSection.GetReloadToken,
				() => TrySetLogLevel(namedLogLevelSection, levelSwitch));
		}

		// the log level must be a valid microsoft level, we have been keeping the log config in the section
		// that the ms libraries will access.
		static void TrySetLogLevel(IConfigurationSection logLevel, LoggingLevelSwitch levelSwitch) {
			if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(logLevel.Value, out var level))
				throw new UnknownLogLevelException(logLevel.Value!, logLevel.Path);

			levelSwitch.MinimumLevel = level switch {
				Microsoft.Extensions.Logging.LogLevel.None => LogEventLevel.Fatal,
				Microsoft.Extensions.Logging.LogLevel.Trace => LogEventLevel.Verbose,
				Microsoft.Extensions.Logging.LogLevel.Debug => LogEventLevel.Debug,
				Microsoft.Extensions.Logging.LogLevel.Information => LogEventLevel.Information,
				Microsoft.Extensions.Logging.LogLevel.Warning => LogEventLevel.Warning,
				Microsoft.Extensions.Logging.LogLevel.Error => LogEventLevel.Error,
				Microsoft.Extensions.Logging.LogLevel.Critical => LogEventLevel.Fatal,
				_ => throw new UnknownLogLevelException(logLevel.Value, logLevel.Path)
			};
		}
	}

	private static LoggerConfiguration StandardLoggerConfiguration =>
		new LoggerConfiguration()
			.Enrich.WithProperty(Constants.SourceContextPropertyName, "KurrentDB")
			.Enrich.WithProcessId()
			.Enrich.WithThreadId()
			.Enrich.FromLogContext();

	private string GetLogFileName(string? log = null) =>
		Path.Combine(_logsDirectory, $"{_componentName}/log{(log == null ? string.Empty : $"-{log}")}.json");

	private static bool Errors(LogEvent e) => e.Exception != null || e.Level >= LogEventLevel.Error;

	public static implicit operator LoggerConfiguration(KurrentLoggerConfiguration configuration) =>
		configuration._loggerConfiguration;
}

class UnknownLogLevelException(string logLevel, string path)
	: InvalidConfigurationException($"Unknown log level: \"{logLevel}\" at \"{path}\". Known log levels: {string.Join(", ", KnownLogLevels)}") {
	static string[] KnownLogLevels => Enum.GetNames(typeof(Microsoft.Extensions.Logging.LogLevel));
}
