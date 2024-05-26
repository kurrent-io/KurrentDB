using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Display;
using static Serilog.Core.Constants;

namespace EventStore.Testing;

public static class Logging {
	static readonly Subject<LogEvent>                       Logs;
	static readonly ConcurrentDictionary<Guid, IDisposable> Subscriptions;
	static readonly MessageTemplateTextFormatter            DefaultFormatter;

	static Logging() {
		Logs           = new();
		Subscriptions    = new();
		DefaultFormatter = new("[{Timestamp:HH:mm:ss.fff} {Level:u3}] {TestRunId} ({ThreadId:000}) {SourceContext} {Message}{NewLine}{Exception}");

		Log.Logger = new LoggerConfiguration()
			.ReadFrom.Configuration(Application.Configuration)
			.WriteTo.Observers(x => x.Subscribe(Logs.OnNext))
			.Enrich.WithProperty(SourceContextPropertyName, "EventStore")
			.CreateLogger();

		SelfLog.Enable(
			msg => Log.Logger
				.ForContext(SourceContextPropertyName, "SelfLog")
				.Debug(msg)
		);
		
		// TODO SS: not calling either events... investigate at a later time...
		
		AppDomain.CurrentDomain.DomainUnload += (_, _) => {
			SelfLog.WriteLine("Domain unload");
			
			Log.CloseAndFlush();
		
			foreach (var sub in Subscriptions)
				ReleaseLogs(sub.Key);
		};
		
		AppDomain.CurrentDomain.ProcessExit += (_, _) => {
			SelfLog.WriteLine("Process exit");
			
			Log.CloseAndFlush();
		
			foreach (var sub in Subscriptions)
				ReleaseLogs(sub.Key);
		};
		
	}

	[ModuleInitializer]
	public static void Initialize() => SelfLog.WriteLine("Logging initialized");

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
		catch(Exception ex) {
			// ignored
			SelfLog.WriteLine(
				"Failed to release logs for test run {0}: {1}", 
				captureId, ex.ToString()
			);
		}
	}
	
	static Guid CaptureLogs(Action<string> write, Guid testRunId) {
		var callContextData = new AsyncLocal<Guid>(
			// args => {
			// 	if (args.ThreadContextChanged && args.CurrentValue == testRunId)
			// 		write($"WTF?! Thread context changed from {args.PreviousValue} to {args.CurrentValue}");
			// }
		) { Value = testRunId };
		
		var testRunIdProperty = new LogEventProperty("TestRunId", new ScalarValue(testRunId));

		var subscription = Logs
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
				catch (Exception ex) {
					SelfLog.WriteLine(
						"Failed to write log event for test run {0}: {1}",
						testRunId, ex.ToString()
					);
				}
			};
	}
}