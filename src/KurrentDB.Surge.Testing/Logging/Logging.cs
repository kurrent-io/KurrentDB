// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable ArrangeTypeMemberModifiers

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Filters;
using Serilog.Formatting;
using Serilog.Templates;

namespace KurrentDB.Surge.Testing;

public static class Logging {
	static readonly Subject<LogEvent> OnNext;

	#region Xunit

	static readonly ConcurrentDictionary<Guid, IDisposable> Subscriptions;
	static readonly ITextFormatter DefaultFormatter;

	static Logging() {
		OnNext           = new();
		Subscriptions    = new();
		DefaultFormatter = new ExpressionTemplate(
            "[{@t:mm:ss.fff} {@l:u3}] ({ThreadId:000}) {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)} {@m}\n{@x}"
        );

		Log.Logger = new LoggerConfiguration()
            .Enrich.WithProperty(Constants.SourceContextPropertyName, "EventStore")
			.ReadFrom.Configuration(Application.Configuration)
            .Filter.ByExcluding(Matching.FromSource("REGULAR-STATS-LOGGER"))
			.WriteTo.Observers(x => x.Subscribe(OnNext.OnNext))
			.CreateLogger();

		AppDomain.CurrentDomain.DomainUnload += (_, _) => {
			foreach (var sub in Subscriptions)
				ReleaseLogs(sub.Key);

            Log.CloseAndFlush();
		};
	}

	public static void Initialize() { } // triggers static ctor

	/// <summary>
	/// Captures logs for the duration of the test run.
	/// </summary>
	public static Guid CaptureLogs(ITestOutputHelper outputHelper, Guid? testRunId = null) =>
		CaptureLogs(outputHelper.WriteLine, testRunId ?? Guid.NewGuid());

	public static void ReleaseLogs(Guid captureId) {
		if (!Subscriptions.TryRemove(captureId, out var subscription))
			return;

		try {
			subscription.Dispose();
		}
		catch {
			// ignored
		}
	}

	static Guid CaptureLogs(Action<string> write, Guid testRunId) {
		var callContextData   = new AsyncLocal<Guid> { Value = testRunId };
		var testRunIdProperty = new LogEventProperty("TestRunId", new ScalarValue(testRunId));

		var subscription = OnNext
			.Where(_ => callContextData.Value.Equals(testRunId))
			.Subscribe(WriteLogEvent());

		Subscriptions.TryAdd(testRunId, subscription);

		return testRunId;

		Action<LogEvent> WriteLogEvent() =>
			logEvent => {
				logEvent.AddPropertyIfAbsent(testRunIdProperty);
				using var writer = new StringWriter();
				DefaultFormatter.Format(logEvent, writer);
				try {
					write(writer.ToString().Trim());
				}
				catch (Exception) {
					// ignored
				}
			};
	}

	#endregion

	#region TUnit

	static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
		.Enrich.WithProperty(Constants.SourceContextPropertyName, nameof(TUnit))
		.Enrich.WithThreadId()
		.Enrich.WithProcessId()
		.Enrich.WithMachineName()
		.Enrich.FromLogContext()
		.Enrich.WithExceptionDetails()
		.MinimumLevel.Verbose();

	public static void Initialize(IConfiguration configuration) {
		// TODO WC: Tests do not run without this
		// EnsureNoConsoleLoggers(configuration);

		Log.Logger = DefaultLoggerConfig
			.ReadFrom.Configuration(configuration)
			.WriteTo.Observers(x => x.Subscribe(OnNext.OnNext))
			.WriteToTUnit()
			.CreateLogger();
	}

	public static IPartitionedLoggerFactory CaptureTestLogs(Guid testUid, Func<Guid> getTestUidFromContext) {
		var testLogger = DefaultLoggerConfig
			.WriteToTUnit(testUid)
			.CreateLogger();

		return new SerilogPartitionedLoggerFactory(
			"TestUid", testUid, testLogger, OnNext,
			state => getTestUidFromContext().Equals(state.PartitionId)
		);
	}

	public static void CloseAndFlush() => Log.CloseAndFlush();

	static LoggerConfiguration WriteToTUnit(this LoggerConfiguration config, Guid? testUid = null) {
		return testUid is not null
			? config
				.Enrich.WithProperty("TestUid", testUid)
				.WriteToTUnitConsole()
			: config.WriteTo.Logger(
				cfg => cfg.Filter
					.ByExcluding(logEvent => logEvent.Properties.ContainsKey("TestUid"))
					.WriteToTUnitConsole()
			);
	}

	static LoggerConfiguration WriteToTUnitConsole(this LoggerConfiguration config) =>
		config.WriteTo.Console(
			theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
			outputTemplate:
			"[{Timestamp:mm:ss.fff} {Level:u3}] {TestUid} {MachineName} ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
			applyThemeToRedirectedOutput: true
		);

	static void EnsureNoConsoleLoggers(IConfiguration configuration) {
		var consoleLoggerEntries = configuration.AsEnumerable()
			.Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();

		if (consoleLoggerEntries.Count != 0)
			throw new InvalidOperationException("Console loggers are not allowed in the configuration");
	}

	#endregion
}
