// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;
using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Metrics;

// using observable counters rather than regular to reduce the cost of incrementing
// (although, its probably not important at the rate calls are created)
// works in a similar way to the PollingCounters here
// https://github.com/grpc/grpc-dotnet/blob/master/src/Grpc.AspNetCore.Server/Internal/GrpcEventSource.cs
public class IncomingGrpcCallsMetric : EventListener {
	private long _callsCurrent;
	private long _callsTotal;
	private long _callsFailed;
	private long _callsUnimplemented;
	private long _callsDeadlineExceeded;

	public IncomingGrpcCallsMetric(
		Meter meter,
		string currentCallsMetricName,
		string totalCallsMetricName,
		IncomingGrpcCall[] filter) {

		if (filter.Contains(IncomingGrpcCall.Current)) {
			meter.CreateObservableUpDownCounter(
				currentCallsMetricName,
				() => Volatile.Read(ref _callsCurrent));
		}

		var funcs = new List<Func<Measurement<long>>>();

		if (filter.Contains(IncomingGrpcCall.Total)) {
			var tags = GenTags("total");
			funcs.Add(() => new(Volatile.Read(ref _callsTotal), tags.AsSpan()));
		}

		if (filter.Contains(IncomingGrpcCall.Failed)) {
			var tags = GenTags("failed");
			funcs.Add(() => new(Volatile.Read(ref _callsFailed), tags.AsSpan()));
		}

		if (filter.Contains(IncomingGrpcCall.Unimplemented)) {
			var tags = GenTags("unimplemented");
			funcs.Add(() => new(Volatile.Read(ref _callsUnimplemented), tags.AsSpan()));
		}

		if (filter.Contains(IncomingGrpcCall.DeadlineExceeded)) {
			var tags = GenTags("deadline-exceeded");
			funcs.Add(() => new(Volatile.Read(ref _callsDeadlineExceeded), tags.AsSpan()));
		}

		meter.CreateObservableCounter(totalCallsMetricName, GenObserveCalls(funcs.ToArray()));
	}

	private static KeyValuePair<string, object>[] GenTags(string kind) =>
		new KeyValuePair<string, object>[] { new("kind", kind) };

	private static Func<IEnumerable<Measurement<long>>> GenObserveCalls(
		Func<Measurement<long>>[] funcs) {

		var measurements = new Measurement<long>[funcs.Length];

		return () => {
			for (var i = 0; i < measurements.Length; i++) {
				measurements[i] = funcs[i]();
			}
			return measurements;
		};
	}

	protected override void OnEventSourceCreated(EventSource eventSource) {
		if (eventSource.Name != "Grpc.AspNetCore.Server")
			return;

		EnableEvents(eventSource, EventLevel.Verbose);
	}

	protected override void OnEventWritten(EventWrittenEventArgs eventData) {
		switch (eventData.EventName) {
			case "CallStart":
				Interlocked.Increment(ref _callsTotal);
				Interlocked.Increment(ref _callsCurrent);
				break;

			case "CallStop":
				Interlocked.Decrement(ref _callsCurrent);
				break;

			case "CallFailed":
				Interlocked.Increment(ref _callsFailed);
				break;

			case "CallDeadlineExceeded":
				Interlocked.Increment(ref _callsDeadlineExceeded);
				break;

			case "CallUnimplemented":
				Interlocked.Increment(ref _callsUnimplemented);
				break;
		}
	}
}
