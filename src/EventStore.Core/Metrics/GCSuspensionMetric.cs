#nullable enable

using System;
using System.Diagnostics.Tracing;
using EventStore.Core.Bus;
using Serilog;

namespace EventStore.Core.Metrics;

// Track in a metric the recent max duration of execution engine pauses
// Log whenever execution engine pauses take more than a threshold or higher threshold
// Log whenever we start/stop a full blocking GC.
// The log messages are important in case the metric is disabled, not being observed, or
// in case the GC is followed by an election & truncation that prevents it from being observed.
public class GcSuspensionMetric(DurationMaxTracker? tracker) : EventListener {
	private static readonly ILogger Log = Serilog.Log.ForContext<GcSuspensionMetric>();

	// Match DefaultSlowMessageThreshold so slow messages can be attributed to GC.
	private static readonly TimeSpan LongSuspensionThreshold = InMemoryBus.DefaultSlowMessageThreshold;
	private static readonly TimeSpan VeryLongSuspensionThreshold = TimeSpan.FromMilliseconds(600);

	// For const values
	// https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
	// seems to be mostly more up to date than
	// https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-garbage-collection-events
	private const int GcKeyword = 0x0000001;
	private const int GCStart = 1;
	private const int GCEnd = 2;
	private const int GCSuspendEEBegin = 9;
	private const int GCRestartEEEnd = 3;

	enum GCStartReason : uint {
		SmallObjectHeapAllocation = 0,
		Induced = 1,
		LowMemory = 2,
		Empty = 3,
		LargeObjectHeapAllocation = 4,
		OutOfSpaceForSmallObjectHeap = 5,
		OutOfSpaceForLargeObjectHeap = 6,
		InducedButNotForcedAsBlocking = 7,
		StressTesting = 8,
		FinalizerInduced = 9,
		UserCodeInducedCompacting = 10,
	}

	enum GCStartType : uint {
		BlockingOutsideBackgroundGC = 0,
		BackgroundGC = 1,
		BlockingDuringBackgroundGC = 2,
	}

	enum GCSuspendReason : uint {
		Other = 0,
		GarbageCollection = 1,
		AppDomainShutdown = 2,
		CodePitching = 3,
		Shutdown = 4,
		Debugger = 5,
		GarbageCollectionPreparation = 6,
		DebuggerSweep = 7,
	}

	// the current full blocking gc
	private DateTime? _fullGCStarted;
	private uint? _fullGCNumber;

	// the current suspension
	private DateTime? _suspendStarted;
	private GCSuspendReason? _suspendReason;

	protected override void OnEventSourceCreated(EventSource eventSource) {
		if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime")) {
			EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)GcKeyword);
		}
	}

	protected override void OnEventWritten(EventWrittenEventArgs eventData) {
		switch (eventData.EventId) {
			case GCStart: {
				var payload = eventData.Payload!;
				var payloadNames = eventData.PayloadNames!;

				var gcNumber = default(uint?);
				var generation = default(uint?);
				var reason = default(GCStartReason?);
				var type = default(GCStartType?);

				for (var i = 0; i < payloadNames.Count; i++) {
					switch (payloadNames[i]) {
						case "Count": gcNumber = (uint)payload[i]!; break;
						case "Depth": generation = (uint)payload[i]!; break;
						case "Reason": reason = (GCStartReason)payload[i]!; break;
						case "Type": type = (GCStartType)payload[i]!; break;
					}
				}

				if (generation >= 2 && type
						is GCStartType.BlockingOutsideBackgroundGC
						or GCStartType.BlockingDuringBackgroundGC) {

					Log.Information("Start of full blocking garbage collection. GC: #{GCNumber}. Generation: {Generation}. Reason: {Reason}. Type: {Type}.",
						gcNumber, generation, reason, type);

					_fullGCStarted = eventData.TimeStamp;
					_fullGCNumber = gcNumber;
				}

				break;
			}

			case GCEnd: {
				var gcNumber = (uint)eventData.Payload![eventData.PayloadNames!.IndexOf("Count")]!;

				if (gcNumber == _fullGCNumber && _fullGCStarted is { } started) {
					var elapsed = eventData.TimeStamp.Subtract(started);

					Log.Information("End of full blocking garbage collection. GC: #{GCNumber}. Took: {Elapsed:N0}ms",
						gcNumber, elapsed.TotalMilliseconds);

					_fullGCStarted = null;
					_fullGCNumber = null;
				}

				break;
			}

			case GCSuspendEEBegin: {
				_suspendReason = (GCSuspendReason)eventData.Payload![eventData.PayloadNames!.IndexOf("Reason")]!;
				_suspendStarted = eventData.TimeStamp;
				break;
			}

			case GCRestartEEEnd: {
				if (_suspendStarted is not { } started) {
					Log.Warning(
						"Unexpected garbage collection GCRestartEEEnd event. Started: {Started}. Reason: {Reason}",
						_suspendStarted, _suspendReason);
					return;
				}

				var elapsed = eventData.TimeStamp.Subtract(started);

				// We used to only track suspensions that are meant for garbage collection, because that is the meaning of the metric
				// However, Microsoft views these all as related to GC (it's a GC message), so it is reasonable for us to include them all.
				// We are now capturing the reason for long suspensions in the log, so a long suspension that is not directly related to GC
				// will be explainable.
				tracker?.RecordNow(elapsed);

				if (elapsed >= LongSuspensionThreshold) {
					var veryLong = elapsed >= VeryLongSuspensionThreshold;
					Log.Write(
						veryLong
							? Serilog.Events.LogEventLevel.Warning
							: Serilog.Events.LogEventLevel.Information,
						"Garbage collection: "
							+ (veryLong ? "Very long" : "Long")
							+ " Execution Engine Suspension. Reason: {Reason}. Took: {Elapsed:N0}ms",
						_suspendReason, elapsed.TotalMilliseconds);
				}

				_suspendStarted = null;
				_suspendReason = null;
				break;
			}
		}
	}
}
