// using System.Reactive.Linq;
// using System.Reactive.Subjects;
// using Microsoft.Extensions.Configuration;
// using Serilog;
// using Serilog.Configuration;
// using Serilog.Context;
// using Serilog.Core;
// using Serilog.Core.Enrichers;
// using Serilog.Events;
// using Serilog.Exceptions;
// using Serilog.Extensions.Logging;
// using ILogger = Serilog.ILogger;
//
// namespace KurrentDB.Testing.Logging;
//
// public static class LoggingContext {
// 	static readonly PropertyEnricher DefaultSourceContextEnricher = new(Constants.SourceContextPropertyName, nameof(TUnit));
//
//     static LoggerConfiguration DefaultLoggerConfig => new LoggerConfiguration()
//         .Enrich.With(DefaultSourceContextEnricher)
//         .Enrich.WithThreadId()
//         .Enrich.WithProcessId()
//         .Enrich.WithMachineName()
//         .Enrich.FromLogContext()
//         .Enrich.WithExceptionDetails()
//         .MinimumLevel.Verbose();
//
//     public static Subject<LogEvent> LogEvents { get; } = new();
//
//     static IConfiguration Configuration { get; set; } = null!;
//
//     static int Inititalized;
//
//     public static void Initialize(IConfiguration configuration) {
// 	    if (Interlocked.CompareExchange(ref Inititalized, 1, 0) != 0)
// 		    throw new InvalidOperationException("LoggingContext is already initialized");
//
// 	    Configuration =	configuration;
//
// 	    EnsureNoConsoleLoggers(configuration);
//
//         Log.Logger = DefaultLoggerConfig
//             .ReadFrom.Configuration(configuration)
//             .Enrich.WithProperty("TestUid", Guid.Empty)
//             .WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
//             .WriteTo.Console()
//             .CreateLogger();
//
//         return;
//
//         static void EnsureNoConsoleLoggers(IConfiguration configuration) {
// 	        var consoleLoggerEntries = configuration.AsEnumerable()
// 		        .Where(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console").ToList();
//
// 	        if (consoleLoggerEntries.Count != 0)
// 		        throw new InvalidOperationException("Console loggers are not allowed in the configuration");
//         }
//     }
//
//     static LoggerConfiguration Console(this LoggerSinkConfiguration config) =>
// 	    config.Console(
// 		    theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
// 		    outputTemplate: "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
// 		    applyThemeToRedirectedOutput: true
// 	    );
//
//
//     public static ValueTask CloseAndFlushAsync() => Log.CloseAndFlushAsync();
//
//     /// <summary>
//     /// Correlates all logs for the current context and enriches them with a property containing the specified correlation ID.
//     /// </summary>
//     /// <param name="correlationId">
//     /// The correlation ID to use for enriching log events.
//     /// </param>
//     /// <param name="propertyName">
//     /// The name of the property to use for the correlation ID. Default is "CorrelationId".
//     /// </param>
//     /// <param name="isMatch">
//     /// A function that determines whether a log event should be enriched with the correlation ID property.
// 	/// The function receives the log event as input and should return true if the event should be enriched, false otherwise.
//     /// </param>
//     public static ILogger CaptureLogs(Guid correlationId, string propertyName, Func<Guid, LogEvent, bool> isMatch) {
// 		// Create a logger that writes to the console and enriches logs with the correlation ID
// 	    var logger = DefaultLoggerConfig
// 			.ReadFrom.Configuration(Configuration)
// 			.Enrich.WithProperty(propertyName, correlationId)
// 			.WriteTo.Console()
// 			.CreateLogger();
//
// 		// Reuse the property instance to reduce allocations
// 	    var prop = new LogEventProperty(propertyName, new ScalarValue(correlationId));
//
// 	    // Subscribe to log events and enrich matching events with the correlation ID property
// 	    _ = LogEvents
// 		    // ...what if by some reason the property is already there and its not our log? must test this thoroughly
// 		    .Where(logEvent => !AlreadyCorrelated(logEvent) && isMatch(correlationId, logEvent))
// 		    .Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));
//
// 	    return logger;
//
// 	    bool AlreadyCorrelated(LogEvent logEvent) =>
// 		    logEvent.Properties.TryGetValue(propertyName, out var existing)
// 		 && existing is ScalarValue { Value: Guid existingGuid }
// 		 && existingGuid != Guid.Empty && existingGuid != correlationId;
//     }
//
//     // public record ScopedLoggerContext(ILogger Logger, IDisposable Subscription, IDisposable Context) : IDisposable {
// 	   //  public void Dispose() {
// 		  //   Subscription.Dispose();
// 		  //   Context.Dispose();
// 	   //  }
//     // }
//
//     // public static ScopedLoggerContext CreateScopedLogger(Guid correlationId, string propertyName) {
// 	   //  // Create a logger that writes to the console and enriches logs with the correlation ID
// 	   //  var logger = DefaultLoggerConfig
// 		  //   .ReadFrom.Configuration(Configuration)
// 		  //   .Enrich.WithProperty(propertyName, correlationId)
// 		  //   .WriteTo.Console()
// 		  //   .CreateLogger();
//     //
// 	   //  var prop = new LogEventProperty(propertyName, new ScalarValue(correlationId));
//     //
// 	   //  var subscription = LogEvents
// 		  //   // ...what if by some reason the property is already there and its not our log? must test this thoroughly
// 		  //   .Where(logEvent => !AlreadyCorrelated(logEvent) && isMatch(correlationId, logEvent))
// 		  //   .Subscribe(logEvent => logEvent.AddOrUpdateProperty(prop));
//     //
// 	   //  return logger;
//     // }
//     //
//     // public class MicrosoftDependencyInjectionDataSourceAttribute : DependencyInjectionDataSourceAttribute<IServiceScope> {
// 	   //  static readonly IServiceProvider ServiceProvider = CreateServiceProvider();
//     //
// 	   //  public override IServiceScope CreateScope(DataGeneratorMetadata dataGeneratorMetadata) =>
// 		  //   ServiceProvider.CreateAsyncScope();
//     //
// 	   //  public override object? Create(IServiceScope scope, Type type) =>
// 		  //   scope.ServiceProvider.GetService(type);
//     //
// 	   //  static IServiceProvider CreateServiceProvider()
// 	   //  {
// 		  //   return new ServiceCollection()
// 			 //    //.AddTransient()
// 			 //    .BuildServiceProvider();
// 	   //  }
//     // }
// }
