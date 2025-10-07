// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace Kurrent.Surge.Testing.TUnit.Logging;

public static class Logging {
    static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
        .Enrich.WithProperty(Constants.SourceContextPropertyName, nameof(TUnit))
        .Enrich.WithThreadId()
        .Enrich.WithProcessId()
        .Enrich.WithMachineName()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .MinimumLevel.Debug();

    static Subject<LogEvent> OnNext { get; } = new();

    static IConfiguration Configuration { get; set; } = null!;

    public static void Initialize(IConfiguration configuration) {
	    Configuration =	configuration;

	    EnsureNoConsoleLoggers(configuration);

        Log.Logger = DefaultLoggerConfig
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("TestUid", Guid.Empty)
            .Enrich.WithProperty("ShortTestUid", "000000000000")
            .WriteTo.Observers(o => o.Subscribe(OnNext.OnNext))
            .WriteTo.Logger(cfg => cfg.WriteTo.Console())
            .CreateLogger();
    }

    public static ILoggerFactory CaptureTestLogs(Guid testUid, Func<LogEvent, bool> isMatch) {
	    ILogger logger = DefaultLoggerConfig
		    .ReadFrom.Configuration(Configuration)
		    .Enrich.WithProperty("TestUid", testUid)
		    .Enrich.WithProperty("ShortTestUid", testUid.ToString()[^12..])
		    .WriteTo.Console()
		    .CreateLogger();

	    var testUidProp      = new LogEventProperty("TestUid", new ScalarValue(testUid));
	    var shortTestUidProp = new LogEventProperty("ShortTestUid", new ScalarValue(testUid.ToString()[^12..]));

	    _ = OnNext
		    .Where(isMatch)
		    .Subscribe(logEvent => {
			    logEvent.AddOrUpdateProperty(testUidProp);
			    logEvent.AddOrUpdateProperty(shortTestUidProp);
		    });

	    return new SerilogLoggerFactory(logger);
    }

    public static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();

    static LoggerConfiguration Console(this LoggerSinkConfiguration config) =>
	    config.Console(
            theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
            outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
            applyThemeToRedirectedOutput: true
        );

    static void EnsureNoConsoleLoggers(IConfiguration configuration) {
        var consoleLoggerEntries = configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();

        if (consoleLoggerEntries.Count != 0)
            throw new InvalidOperationException("Console loggers are not allowed in the configuration");
    }
}
