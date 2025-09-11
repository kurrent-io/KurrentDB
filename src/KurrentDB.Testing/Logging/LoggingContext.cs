using System.Reactive.Linq;
using System.Reactive.Subjects;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using ILogger = Serilog.ILogger;

namespace KurrentDB.Testing.Logging;

public static class LoggingContext {
    static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
        .Enrich.WithProperty(Constants.SourceContextPropertyName, nameof(TUnit))
        .Enrich.WithThreadId()
        .Enrich.WithProcessId()
        .Enrich.WithMachineName()
        .Enrich.FromLogContext()
        .Enrich.WithExceptionDetails()
        .MinimumLevel.Verbose();

    static Subject<LogEvent> OnNext { get; } = new();

    static IConfiguration Configuration { get; set; } = null!;

    static bool Initialized;

    public static void Initialize(IConfiguration configuration) {
	    if (Initialized)
		   throw new InvalidOperationException("LoggingContext is already initialized");

	    Initialized   = true;
	    Configuration =	configuration;

	    EnsureNoConsoleLoggers(configuration);

        Log.Logger = DefaultLoggerConfig
            .ReadFrom.Configuration(configuration)
            .Enrich.WithProperty("TestUid", Guid.Empty)
            .Enrich.WithProperty("ShortTestUid", "000000000000")
            .WriteTo.Observers(o => o.Subscribe(OnNext.OnNext))
            .WriteTo.Logger(cfg => cfg.WriteTo.Console(
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
                outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
                applyThemeToRedirectedOutput: true
            ))
            .CreateLogger();

        return;

        static void EnsureNoConsoleLoggers(IConfiguration configuration) {
	        var consoleLoggerEntries = configuration.AsEnumerable()
		        .Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();

	        if (consoleLoggerEntries.Count != 0)
		        throw new InvalidOperationException("Console loggers are not allowed in the configuration");
        }
    }

    public static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();

    /// <summary>
    /// Correlates all logs for the current test context and enriches them with TestUid and ShortTestUid properties.
    /// </summary>
    public static void ConfigureLogging(this TestContext context, Guid testUid) {
	    var sourceContext = context.GetDisplayName();

	    // creates and sets up the logger for the test context
	    ILogger logger = DefaultLoggerConfig
		    .ReadFrom.Configuration(Configuration)
		    .Enrich.WithProperty(Constants.SourceContextPropertyName, sourceContext)
		    .Enrich.WithProperty("TestUid", testUid)
		    .Enrich.WithProperty("ShortTestUid", testUid.ToString()[^12..])
		    .WriteTo.Console()
		    .CreateLogger();

	    context.AssignLogger(logger);

	    var testUidProp      = new LogEventProperty("TestUid", new ScalarValue(testUid));
	    var shortTestUidProp = new LogEventProperty("ShortTestUid", new ScalarValue(testUid.ToString()[^12..]));

	    _ = OnNext
		    .Where(_ => TestContext.Current.TestUid(Guid.Empty).Equals(testUid))
		    .Subscribe(logEvent => {
			    logEvent.AddOrUpdateProperty(testUidProp);
			    logEvent.AddOrUpdateProperty(shortTestUidProp);
		    });
    }
}
