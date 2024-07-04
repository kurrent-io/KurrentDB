using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Templates;

namespace EventStore.Testing;

public static class Logging {
	static readonly Subject<LogEvent>                       OnNext;
	static readonly ConcurrentDictionary<Guid, IDisposable> Subscriptions;
	static readonly ITextFormatter                          DefaultFormatter;

	static Logging() {
		OnNext           = new();
		Subscriptions    = new();
		DefaultFormatter = new ExpressionTemplate(
            "[{@t:mm:ss.fff} {@l:u3}] ({ThreadId:000}) {Substring(SourceContext, LastIndexOf(SourceContext, '.') + 1)} {@m}\n{@x}"
        );

		Log.Logger = new LoggerConfiguration()
			.ReadFrom.Configuration(Application.Configuration)
			.WriteTo.Observers(x => x.Subscribe(OnNext.OnNext))
			.CreateLogger();

        Ductus.FluentDocker.Services.Logging.Enabled();

		AppDomain.CurrentDomain.DomainUnload += (_, _) => {
			Log.CloseAndFlush();

			foreach (var sub in Subscriptions)
				ReleaseLogs(sub.Key);
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
}