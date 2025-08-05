// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Subjects;
using KurrentDB.Surge.Testing;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;

namespace Kurrent.Surge.Testing.TUnit.Logging;

public static class Logging {
    static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
        .Enrich.WithProperty(Constants.SourceContextPropertyName, nameof(TUnit))
        .Enrich.WithThreadId()
        .Enrich.WithProcessId()
        .Enrich.WithMachineName()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .MinimumLevel.Verbose();

    static Subject<LogEvent> OnNext { get; } = new();

    public static void Initialize(IConfiguration configuration) {
        EnsureNoConsoleLoggers(configuration);

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
            outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3}] {TestUid} {MachineName} ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
            applyThemeToRedirectedOutput: true
        );

    static void EnsureNoConsoleLoggers(IConfiguration configuration) {
        var consoleLoggerEntries = configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();

        if (consoleLoggerEntries.Count != 0)
            throw new InvalidOperationException("Console loggers are not allowed in the configuration");
    }
}
