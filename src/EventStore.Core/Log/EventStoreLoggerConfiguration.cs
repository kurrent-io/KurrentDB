// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Threading;
using EventStore.Common.Exceptions;
using EventStore.Common.Options;
using EventStore.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Templates;
using Serilog.Templates.Themes;

namespace EventStore.Common.Log;

public class EventStoreLoggerConfiguration {
	static readonly ExpressionTemplate ConsoleOutputExpressionTemplate = new(
		"[{ProcessId,5},{ThreadId,2},{@t:HH:mm:ss.fff},{@l:u3}] {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1), -30} {@m}\n{@x}",
		theme: TemplateTheme.Literate
	);

	private const string CompactJsonTemplate = "{ {@t, @mt, @r, @l, @i, @x, ..@p} }\n";

	public static readonly Logger ConsoleLog = StandardLoggerConfiguration
		.WriteTo.Console(ConsoleOutputExpressionTemplate)
		.CreateLogger();

	private static readonly Func<LogEvent, bool> RegularStats = Matching.FromSource("REGULAR-STATS-LOGGER");

	private static readonly SerilogEventListener EventListener;

	private static int Initialized;
	private static LoggingLevelSwitch _defaultLogLevelSwitch;
	private static object _defaultLogLevelSwitchLock = new object();

	private readonly string _logsDirectory;
	private readonly string _componentName;
	private readonly LoggerConfiguration _loggerConfiguration;

	static EventStoreLoggerConfiguration() {
		Serilog.Log.Logger = ConsoleLog;
		AppDomain.CurrentDomain.UnhandledException += (s, e) => {
			if (e.ExceptionObject is Exception exc)
				Serilog.Log.Fatal(exc, "Global Unhandled Exception occurred.");
			else
				Serilog.Log.Fatal("Global Unhandled Exception object: {e}.", e.ExceptionObject);
		};
		EventListener = new SerilogEventListener();
	}

	public static void Initialize(string logsDirectory, string componentName, LogConsoleFormat logConsoleFormat,
		int logFileSize, RollingInterval logFileInterval, int logFileRetentionCount, bool disableLogFile,
		string logConfig = "logconfig.json") {
		if (Interlocked.Exchange(ref Initialized, 1) == 1) {
			throw new InvalidOperationException($"{nameof(Initialize)} may not be called more than once.");
		}

		if (logsDirectory.StartsWith("~")) {
			throw new ApplicationInitializationException(
				"The given log path starts with a '~'. KurrentDB does not expand '~'.");
		}

		var configurationRoot = new ConfigurationBuilder()
			.AddKurrentConfigFile(logConfig, reloadOnChange: true)
			.Build();

		Serilog.Debugging.SelfLog.Enable(ConsoleLog.Information);

		Serilog.Log.Logger = (configurationRoot.GetSection("Serilog").Exists()
				? new LoggerConfiguration()
					.Enrich.WithProperty(Constants.SourceContextPropertyName, "KurrentDB")
					.ReadFrom.Configuration(configurationRoot)
				: Default(logsDirectory, componentName, configurationRoot, logConsoleFormat, logFileInterval,
					logFileSize, logFileRetentionCount, disableLogFile))
			.CreateLogger();

		Serilog.Debugging.SelfLog.Disable();
	}

	public static bool AdjustMinimumLogLevel(LogLevel logLevel) {
		lock (_defaultLogLevelSwitchLock) {
			#if !DEBUG
			if (_defaultLogLevelSwitch == null) {
				throw new InvalidOperationException("The logger configuration has not yet been initialized.");
			}
			#endif
			if (!Enum.TryParse<LogEventLevel>(logLevel.ToString(), out var serilogLogLevel)) {
				throw new ArgumentException($"'{logLevel}' is not a valid log level.");
			}

			if (serilogLogLevel == _defaultLogLevelSwitch.MinimumLevel) return false;
			_defaultLogLevelSwitch.MinimumLevel = serilogLogLevel;
			return true;
		}
	}

	private static LoggerConfiguration Default(string logsDirectory, string componentName,
		IConfigurationRoot logLevelConfigurationRoot, LogConsoleFormat logConsoleFormat,
		RollingInterval logFileInterval, int logFileSize, int logFileRetentionCount, bool disableLogFile) =>
		new EventStoreLoggerConfiguration(logsDirectory, componentName, logLevelConfigurationRoot, logConsoleFormat,
			logFileInterval, logFileSize, logFileRetentionCount, disableLogFile);

	private EventStoreLoggerConfiguration(string logsDirectory, string componentName,
		IConfigurationRoot logLevelConfigurationRoot, LogConsoleFormat logConsoleFormat,
		RollingInterval logFileInterval, int logFileSize, int logFileRetentionCount, bool disableLogFile) {
		if (logsDirectory == null) {
			throw new ArgumentNullException(nameof(logsDirectory));
		}

		if (componentName == null) {
			throw new ArgumentNullException(nameof(componentName));
		}

		if (logLevelConfigurationRoot == null) {
			throw new ArgumentNullException(nameof(logLevelConfigurationRoot));
		}

		_logsDirectory = logsDirectory;
		_componentName = componentName;

		var loglevelSection = logLevelConfigurationRoot.GetSection("Logging").GetSection("LogLevel");
		var defaultLogLevelSection = loglevelSection.GetSection("Default");
		lock (_defaultLogLevelSwitchLock) {
			_defaultLogLevelSwitch = new LoggingLevelSwitch {
				MinimumLevel = LogEventLevel.Verbose
			};
			ApplyLogLevel(defaultLogLevelSection, _defaultLogLevelSwitch);
		}

		var loggerConfiguration = StandardLoggerConfiguration
			.MinimumLevel.ControlledBy(_defaultLogLevelSwitch)
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
					: new(CompactJsonTemplate));

			if (!disableLogFile) {
				configuration.WriteTo
					.RollingFile(GetLogFileName(), new ExpressionTemplate(CompactJsonTemplate),
						logFileRetentionCount, logFileInterval, logFileSize)
					.WriteTo.Logger(Error);
			}

			configuration.WriteTo.Sink(ObservableSerilogSink.Instance);
		}

		void Error(LoggerConfiguration configuration) {
			if (!disableLogFile) {
				configuration
					.Filter.ByIncludingOnly(Errors)
					.WriteTo
					.RollingFile(GetLogFileName("err"), new ExpressionTemplate(CompactJsonTemplate),
						logFileRetentionCount, logFileInterval, logFileSize);
			}
		}

		void Stats(LoggerConfiguration configuration) {
			if (!disableLogFile) {
				configuration.WriteTo.RollingFile(GetLogFileName("stats"),
					new ExpressionTemplate(CompactJsonTemplate), logFileRetentionCount, logFileInterval,
					logFileSize);
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
				throw new UnknownLogLevelException(logLevel.Value, logLevel.Path);

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


	private string GetLogFileName(string log = null) =>
		Path.Combine(_logsDirectory, $"{_componentName}/log{(log == null ? string.Empty : $"-{log}")}.json");

	private static bool Errors(LogEvent e) => e.Exception != null || e.Level >= LogEventLevel.Error;

	public static implicit operator LoggerConfiguration(EventStoreLoggerConfiguration configuration) =>
		configuration._loggerConfiguration;
}

class UnknownLogLevelException(string logLevel, string path)
	: InvalidConfigurationException($"Unknown log level: \"{logLevel}\" at \"{path}\". Known log levels: {string.Join(", ", KnownLogLevels)}") {
	static string[] KnownLogLevels => Enum.GetNames(typeof(Microsoft.Extensions.Logging.LogLevel));
}
