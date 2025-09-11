// using System.Reactive.Subjects;
// using Microsoft.Extensions.Configuration;
// using Serilog;
// using Serilog.Core;
// using Serilog.Core.Enrichers;
// using Serilog.Events;
// using Serilog.Exceptions;
// using Serilog.Sinks.SystemConsole.Themes;
//
// namespace KurrentDB.Testing.Logging;
//
// public static class AppLogging {
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
//     static readonly object Locker = new();
//
//     static long _initialized;
//
//     public static void Initialize(IConfiguration configuration) {
// 	    if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
// 		    throw new InvalidOperationException("LoggingContext is already initialized! Check your test setup code.");
//
//         lock (Locker) {
//             try {
//                 DenyConsoleSinks(configuration);
//
//                 Log.Logger = DefaultLoggerConfig
//                     .ReadFrom.Configuration(configuration)
//                     .Enrich.WithProperty("TestUid", Guid.Empty)
//                     .WriteTo.Observers(o => o.Subscribe(LogEvents.OnNext))
//                     .WriteTo.Console(
//                         theme: AnsiConsoleTheme.Literate,
//                         outputTemplate:
//                         "[{Timestamp:mm:ss.fff} {Level:u3} {ShortTestUid}] ({ThreadId:000}) {SourceContext} {NewLine}{Message}{NewLine}{Exception}{NewLine}",
//                         applyThemeToRedirectedOutput: true
//                     )
//                     .CreateLogger();
//             }
//             finally {
//                 Interlocked.Exchange(ref _initialized, 0);
//             }
//         }
//
//         return;
//
//         static void DenyConsoleSinks(IConfiguration configuration) {
// 	        var hasConsoleSinks = configuration
//                 .AsEnumerable()
//                 .Any(x => x.Key.StartsWith("Serilog") && x.Key.EndsWith(":Name") && x.Value == "Console");
//
//             if (hasConsoleSinks)
//                 throw new InvalidOperationException("Console sinks are not allowed in the configuration");
//         }
//     }
// }
