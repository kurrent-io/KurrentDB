// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using EventStore.Core.Services.Monitoring.Stats;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Services.Monitoring.Utils;

internal class EventCountersHelper : IDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<EventCountersHelper>();
	private const int InvalidCounterResult = -1;

	private EventPipeSession _session;
	private readonly List<EventPipeProvider> _providers;
	private readonly IDictionary<string, double> _collectedStats = new Dictionary<string, double>();
	private readonly HashSet<string> _gcCollectionNames = new HashSet<string>() {
		"gen-0-gc-count",
		"gen-1-gc-count",
		"gen-2-gc-count"
	};

	private readonly int _pid;

	public EventCountersHelper(long collectIntervalInMs) {
		var currentProcess = Process.GetCurrentProcess();
		_pid = currentProcess.Id;

		_providers = new List<EventPipeProvider> {
			new EventPipeProvider("System.Runtime", EventLevel.Informational,
				(long)(ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.Threading |
					   ClrTraceEventParser.Keywords.Exception | ClrTraceEventParser.Keywords.Contention),
				new Dictionary<string, string> {
					{"EventCounterIntervalSec", $"{collectIntervalInMs / 1000}"}
				}),
			new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Informational,
				(long)ClrTraceEventParser.Keywords.GC, new Dictionary<string, string>())
		};
	}

	public void Start() {
		Task.Run(() => {
			try {
				var client = new DiagnosticsClient(_pid);
				_session = client.StartEventPipeSession(_providers, false);
				using var source = new EventPipeEventSource(_session.EventStream);


				source.Dynamic.All += obj => {
					if (obj.EventName.Equals("EventCounters")) {
						var payload = (IDictionary<string, object>)obj.PayloadValue(0);
						var pairs = (IDictionary<string, object>)(payload["Payload"]);

						var name = string.Intern(pairs["Name"].ToString());

						if (_gcCollectionNames.Contains(name)) {
							var gcCounterType = pairs["CounterType"];
							if (gcCounterType.Equals("Sum")) {
								var gcCollectionsInTimeInterval = double.Parse(pairs["Increment"].ToString());
								_collectedStats.TryGetValue(name, out var previousGcCollectionCount);
								_collectedStats[name] = previousGcCollectionCount + gcCollectionsInTimeInterval;
							}
							return;
						}

						var counterType = pairs["CounterType"];
						if (counterType.Equals("Sum")) {
							_collectedStats[name] = double.Parse(pairs["Increment"].ToString());
						}

						if (counterType.Equals("Mean")) {
							_collectedStats[name] = double.Parse(pairs["Mean"].ToString());
						}
					}
				};

				source.Process();
			} catch (ObjectDisposedException) {
				// ignore exception on shutdown
			} catch (Exception exception) {
				Log.Warning(exception, "Error encountered while processing events");
			}
		});
	}

	///<summary>
	///Total process CPU usage
	///</summary>
	public float GetProcCpuUsage() {
		return (float)GetCounterValue("cpu-usage");
	}

	///<summary>
	///Current thread count
	///</summary>
	public int GetProcThreadsCount() {
		return (int)GetCounterValue("threadpool-thread-count");
	}

	///<summary>
	///Number of exceptions thrown per second
	///</summary>
	public float GetThrownExceptionsRate() {
		return (float)GetCounterValue("exception-count");
	}

	///<summary>
	///The rate at which threads in the runtime attempt to acquire a managed lock unsuccessfully
	///</summary>
	public float GetContentionsRateCount() {
		return (float)GetCounterValue("monitor-lock-contention-count");
	}

	private double GetCounterValue(string name) {
		if (!_collectedStats.TryGetValue(name, out var value)) return InvalidCounterResult;
		return value;
	}

	public GcStats GetGcStats() {
		return new GcStats(
			gcAllocationSpeed: (float)GetCounterValue("alloc-rate"),
			gcFragmentation: (float)GetCounterValue("gc-fragmentation"),
			gcGen0Items: (long)GetCounterValue("gen-0-gc-count"),
			gcGen0Size: (long)GetCounterValue("gen-0-size"),
			gcGen1Items: (long)GetCounterValue("gen-1-gc-count"),
			gcGen1Size: (long)GetCounterValue("gen-1-size"),
			gcGen2Items: (long)GetCounterValue("gen-2-gc-count"),
			gcGen2Size: (long)GetCounterValue("gen-2-size"),
			gcLargeHeapSize: (long)GetCounterValue("loh-size"),
			gcTimeInGc: (long)GetCounterValue("time-in-gc"),
			gcTotalBytesInHeaps: (long)GetCounterValue("gc-heap-size") * 1_000_000);
	}

	public void Dispose() {
		try {
			_session?.Stop();
		} catch (ServerNotAvailableException) {
		}

		_session?.Dispose();
	}
}
