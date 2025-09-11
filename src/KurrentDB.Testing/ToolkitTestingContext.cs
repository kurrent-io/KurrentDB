using System.Reactive.Subjects;
using Bogus;
using KurrentDB.Testing.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;

using static Serilog.Core.Constants;

namespace KurrentDB.Testing;

public static class ToolkitTestingContext {
    static long _initialized;

    /// <summary>
    /// A shared instance of the Faker class from the Bogus library for generating fake data.
    /// </summary>
    public static Faker Faker { get; } = new();

    /// <summary>
    /// The application's configuration settings, built from various sources such as JSON files and environment variables.
    /// </summary>
    public static IConfiguration Configuration { get; private set; } = null!;

    /// <summary>
    /// An observable subject that emits log events.
    /// </summary>
    public static Subject<LogEvent> LogEvents { get; private set; } = null!;

    public static void Initialize() {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("TestingContext is already initialized! Check your test setup code.");

        new OtelServiceMetadata("TUnit") {
            ServiceVersion   = "0.0.0",
            ServiceNamespace = "KurrentDB.Testing",
        }.UpdateEnvironmentVariables();

        InitConfiguration();
        InitLogging();
    }

    /// <summary>
    /// Subscribes to log events and adds a "TestUid" property to each event that matches the given predicate.
    /// </summary>
    /// <param name="testUid">
    /// The unique identifier for the test, which will be added as a property to the log events.
    /// </param>
    /// <param name="predicate">
    /// A predicate function to filter log events.
    /// Only events for which this function returns true will be enriched with the "TestUid" property.
    /// </param>
    /// <returns>
    /// An IDisposable that can be used to unsubscribe from the log events.
    /// </returns>
    public static IDisposable AttachTestUidToLogEvents(string testUid, Predicate<LogEvent> predicate) {
        var prop = new LogEventProperty(nameof(TestUid), new ScalarValue(testUid));

        return LogEvents.Subscribe(logEvent => {
            if (predicate(logEvent))
                logEvent.AddOrUpdateProperty(prop);
        });
    }

    /// <summary>
    /// Subscribes to all log events and adds a "TestUid" property to each event.
    /// </summary>
    /// <param name="testUid">
    /// The unique identifier for the test, which will be added as a property to the log events.
    /// </param>
    /// <returns>
    /// An IDisposable that can be used to unsubscribe from the log events.
    /// </returns>
    public static IDisposable AttachTestUidToLogEvents(string testUid) =>
        AttachTestUidToLogEvents(testUid, static _ => true);

    /// <summary>
    /// Creates a SerilogLoggerProvider configured with the specified test UID and source context.
    /// This provider can be used to log messages with context about the specific test being executed.
    /// </summary>
    /// <param name="testUid">
    /// The unique identifier for the test, which will be added as a property to the log events.
    /// </param>
    /// <param name="sourceContext">
    /// The source context (usually the class or component name) to be included in the log events.
    /// </param>
    /// <returns>
    /// A disposable logger provider.
    /// </returns>
    public static SerilogLoggerProvider CreateLoggerProvider(string testUid, string? sourceContext) {
        var logger = Log.Logger.ForContext(nameof(TestUid), testUid);

        if (sourceContext is not null)
            logger = logger.ForContext(SourceContextPropertyName, sourceContext);

        return new(logger, dispose: true);
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

    static void InitLogging() {
        DenyConsoleSinks(Configuration);

        const string consoleOutputTemplate =
            "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}";

        LogEvents = new();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty(SourceContextPropertyName, nameof(TUnit))
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithMachineName()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Enrich.WithProperty("TestUid", Guid.Empty)
            .ReadFrom.Configuration(Configuration)
            .WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
            .WriteTo.Console(
                theme: AnsiConsoleTheme.Literate,
                outputTemplate: consoleOutputTemplate,
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
}
