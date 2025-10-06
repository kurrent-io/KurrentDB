// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reactive.Subjects;
using System.Reflection;
using KurrentDB.Testing.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Sinks.SystemConsole.Themes;

using static Serilog.Core.Constants;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Testing;

public static class ToolkitTestEnvironment {
    static long _initialized;

    /// <summary>
    /// An observable subject that emits all log events.
    /// </summary>
    static Subject<LogEvent> LogEvents { get; } = new();

    /// <summary>
    /// The application's configuration settings, built from various sources such as JSON files and environment variables.
    /// </summary>
    public static IConfiguration Configuration { get; private set; } = null!;

    /// <summary>
    /// Initializes the testing environment, setting up configuration and logging.
    /// This method should be called once before any tests are executed.
    /// </summary>
    public static ValueTask Initialize(Assembly assembly) {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("TestingContext is already initialized! Check your test setup code.");

        new OtelServiceMetadata("Toolkit") {
            ServiceVersion   = "1.0.0",
            ServiceNamespace = "KurrentDB.Testing",
        }.UpdateEnvironmentVariables();

        InitConfiguration();
        InitLogging();

        Log.Verbose("{AssemblyName} | Test environment initialized", assembly.GetName().Name);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Resets the testing environment by closing and flushing the Serilog logger.
    /// </summary>
    public static async ValueTask Reset(Assembly assembly) {
        Log.Verbose("{AssemblyName} | Test environment reseting...", assembly.GetName().Name);
        await Log.CloseAndFlushAsync();
        Interlocked.Exchange(ref _initialized, 0);
    }

    static void InitConfiguration() {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        Configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", true)
            .AddJsonFile($"appsettings.{environment}.json", true)                    // Accept default naming convention
            .AddJsonFile($"appsettings.{environment.ToLowerInvariant()}.json", true) // Linux is case-sensitive
            .AddEnvironmentVariables()
            .Build();
    }

    const string ConsoleOutputTemplate =
        "[{Timestamp:mm:ss.fff} {Level:u3}] {TestUid} ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}";

    static void InitLogging() {
        DenyConsoleSinks(Configuration);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithMachineName()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Enrich.WithDemystifiedStackTraces()
            .ReadFrom.Configuration(Configuration)
            .Enrich.WithProperty(SourceContextPropertyName, nameof(ToolkitTestEnvironment))
            .Enrich.WithProperty(nameof(TestUid), TestUid.Empty)
            .WriteTo.OpenTelemetry()
            .WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Literate,
                outputTemplate: ConsoleOutputTemplate,
                applyThemeToRedirectedOutput: true
            )
            .CreateLogger();

        return;

        static void DenyConsoleSinks(IConfiguration configuration) {
            var hasConsoleSinks = configuration.AsEnumerable()
                .Any(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console");

            if (hasConsoleSinks)
                throw new InvalidOperationException("Console sinks are not allowed in the configuration");
        }
    }

    public static (ILogger Logger, IAsyncDisposable Release) CaptureTestLogs(string testUid, string? sourceContext) {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithMachineName()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Enrich.WithDemystifiedStackTraces()
            .ReadFrom.Configuration(Configuration)
            .Enrich.WithProperty(SourceContextPropertyName, sourceContext ?? nameof(ToolkitTestEnvironment))
            .Enrich.WithProperty(nameof(TestUid), testUid)
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Literate,
                outputTemplate: ConsoleOutputTemplate,
                applyThemeToRedirectedOutput: true
            )
            .CreateLogger();

        // var prop = new LogEventProperty(nameof(TestUid), new ScalarValue(testUid));
        // var sub = LogEvents
        //     .Do(logEvent => {
        //         if (TestContext.Current.TryExtractItem<TestUid>("$TestUid", out var uid)) {
        //             if (uid.ToString() == testUid) {
        //                 logEvent.AddOrUpdateProperty(prop);
        //             }
        //         }
        //
        //
        //
        //         // if (TestContext.Current.TestUid() == testUid)
        //         //     logEvent.AddOrUpdateProperty(prop);
        //     })
        //     .Subscribe();

        var disposable = new Disposable(async () => {
                //sub.Dispose();
                logger.Information("Disposing test logger for {TestUid}", testUid);
                await logger.DisposeAsync();
            }
        );

        return (logger, disposable);
    }
}

// public sealed class TUnitLoggerWrapper : Microsoft.Extensions.Logging.ILogger {
//     static readonly TUnitLoggerWrapper _instance = new();
//     public static   TUnitLoggerWrapper Instance => _instance;
//
//     public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;
//
//     public bool IsEnabled(LogLevel logLevel) => TestContext.Current?.GetDefaultLogger()?.IsEnabled((LogLevel)(int)logLevel) == true;
//
//     public void Log<TState>(
//         LogLevel logLevel, EventId eventId, TState state, Exception? exception,
//         Func<TState, Exception?, string> formatter
//     ) {
//         var defaultLogger = TestContext.Current?.GetDefaultLogger();
//         if (defaultLogger == null || !defaultLogger.IsEnabled((LogLevel)(int)logLevel)) return;
//
//         defaultLogger.Log(
//             (LogLevel)(int)logLevel, state, exception,
//             formatter
//         );
//     }
// }
//
// [ProviderAlias("TUnit")]
// public sealed class TUnitLoggerProvider : ILoggerProvider {
//     public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => TUnitLoggerWrapper.Instance;
//     public void                                 Dispose()                         { }
// }
