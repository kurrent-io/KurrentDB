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
	private static readonly TimeSpan LongSuspensionLogPeriod = TimeSpan.FromMilliseconds(10_000);

	// For const values
	// https://learn.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
	// seems to be mostly more up to date than
	// https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-garbage-collection-events
	// but the sources are more authoritative
	// runtime: https://github.com/dotnet/runtime/blob/release/8.0/src/coreclr/gc/gc.h
	// perfview: https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Parsers/ClrEtwAll.cs.base
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
		LowMemoryBlocking = 9,
		UserCodeInducedCompacting = 10, // 10 is correct, doc saying 0x10 is incorrect
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

	// long suspension log aggregation
	private DateTime _lastLog;
	private TimeSpan _periodLongSuspensionsElapsedTotal;
	private int _periodLongSuspensionCount;

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

					Log.Information("Start of full blocking garbage collection at {TimeStamp}. GC: #{GCNumber}. Generation: {Generation}. Reason: {Reason}. Type: {Type}.",
						eventData.TimeStamp, gcNumber, generation, reason, type);

					_fullGCStarted = eventData.TimeStamp;
					_fullGCNumber = gcNumber;
				}

				break;
			}

			case GCEnd: {
				var gcNumber = (uint)eventData.Payload![eventData.PayloadNames!.IndexOf("Count")]!;

				if (gcNumber == _fullGCNumber && _fullGCStarted is { } started) {
					var elapsed = eventData.TimeStamp.Subtract(started);

					Log.Information("End of full blocking garbage collection at {TimeStamp}. GC: #{GCNumber}. Took: {Elapsed:N0}ms",
						eventData.TimeStamp, gcNumber, elapsed.TotalMilliseconds);

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

				if (elapsed >= VeryLongSuspensionThreshold) {
					Log.Warning(
						"Garbage collection: Very long Execution Engine Suspension. Reason: {Reason}. Took: {Elapsed:N0}ms",
						_suspendReason, elapsed.TotalMilliseconds);
				} else if (elapsed >= LongSuspensionThreshold) {
					// long but not VERY long. Aggregate these in case of a scenario where the threshold is regularly exceeded.
					_periodLongSuspensionCount++;
					_periodLongSuspensionsElapsedTotal += elapsed;
					var now = DateTime.Now;
					if (now - _lastLog > LongSuspensionLogPeriod) {
						Log.Information(
							"Garbage collection: Long Execution Engine Suspensions. Last Reason: {Reason}. {Count} long suspensions took: {Elapsed:N0}ms each on average",
							_suspendReason, _periodLongSuspensionCount, _periodLongSuspensionsElapsedTotal.TotalMilliseconds / _periodLongSuspensionCount);

						_lastLog = now;
						_periodLongSuspensionCount = 0;
						_periodLongSuspensionsElapsedTotal = TimeSpan.Zero;
					}
				}

				_suspendStarted = null;
				_suspendReason = null;
				break;
			}
		}
	}
}
