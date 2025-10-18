// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Metrics;

namespace KurrentDB.Common.Configuration;

public class MetricsConfiguration {
	public static MetricsConfiguration Get(IConfiguration configuration) =>
		configuration
			.GetSection("KurrentDB:Metrics")
			.Get<MetricsConfiguration>() ?? new();

	public enum StatusTracker {
		Index = 1,
		Node,
		Scavenge,
	}

	public enum Checkpoint {
		Chaser = 1,
		Epoch,
		Index,
		Proposal,
		Replication,
		StreamExistenceFilter,
		Truncate,
		Writer,
	}

	public enum IncomingGrpcCall {
		Current = 1,
		Total,
		Failed,
		Unimplemented,
		DeadlineExceeded,
	}

	public enum GrpcMethod {
		StreamRead = 1,
		StreamAppend,
		StreamBatchAppend,
		StreamDelete,
		StreamTombstone,
	}

	public enum GossipTracker {
		PullFromPeer = 1,
		PushToPeer,
		ProcessingPushFromPeer,
		ProcessingRequestFromPeer,
		ProcessingRequestFromGrpcClient,
		ProcessingRequestFromHttpClient,
	}

	public enum WriterTracker {
		FlushSize = 1,
		FlushDuration,
	}

	public enum EventTracker {
		Read = 1,
		Written,
	}

	public enum Cache {
		StreamInfo = 1,
		Chunk,
	}

	public enum KestrelTracker {
		ConnectionCount = 1,
	}

	public enum SystemTracker {
		Cpu = 1,
		LoadAverage1m,
		LoadAverage5m,
		LoadAverage15m,
		FreeMem,
		TotalMem,
		DriveTotalBytes,
		DriveUsedBytes,
	}

	public enum ProcessTracker {
		UpTime = 1,
		Cpu,
		MemWorkingSet,
		MemPagedBytes,
		MemVirtualBytes,
		ThreadCount,
		ThreadPoolPendingWorkItemCount,
		LockContentionCount,
		ExceptionCount,
		Gen0CollectionCount,
		Gen1CollectionCount,
		Gen2CollectionCount,
		Gen0Size,
		Gen1Size,
		Gen2Size,
		LohSize,
		TimeInGc,
		HeapSize,
		HeapFragmentation,
		TotalAllocatedBytes,
		DiskReadBytes,
		DiskReadOps,
		DiskWrittenBytes,
		DiskWrittenOps,
		GcPauseDuration,
	}

	public enum QueueTracker {
		Length = 1,
		Processing,
	}

	public class LabelMappingCase {
		public string Regex { get; set; } = "";
		public string Label { get; set; } = "";
	}

	private const string LegacyCoreMeterName = "EventStore.Core";
	private const string NormalCoreMeterName = "KurrentDB.Core";
	private const string LegacyProjectionsMeterName = "EventStore.Projections.Core";
	private const string NormalProjectionsMeterName = "KurrentDB.Projections.Core";

	public bool LegacyCoreNaming =>
		Meters.Contains(LegacyCoreMeterName) &&
		!Meters.Contains(NormalCoreMeterName);

	public bool LegacyProjectionsNaming =>
		Meters.Contains(LegacyProjectionsMeterName) &&
		!Meters.Contains(NormalProjectionsMeterName);

	public string CoreMeterName => LegacyCoreNaming
		? LegacyCoreMeterName
		: NormalCoreMeterName;

	public string ProjectionsMeterName => LegacyProjectionsNaming
		? LegacyProjectionsMeterName
		: NormalProjectionsMeterName;

	public string ServiceName => LegacyCoreNaming && LegacyProjectionsNaming
		? "eventstore"
		: "kurrentdb";

	public string[] Meters { get; set; } = Array.Empty<string>();

	public Dictionary<StatusTracker, bool> Statuses { get; set; } = new();

	public Dictionary<Checkpoint, bool> Checkpoints { get; set; } = new();

	public Dictionary<IncomingGrpcCall, bool> IncomingGrpcCalls { get; set; } = new();

	public Dictionary<GrpcMethod, string> GrpcMethods { get; set; } = new();

	public Dictionary<GossipTracker, bool> Gossip { get; set; } = new();

	public Dictionary<KestrelTracker, bool> Kestrel { get; set; } = new();

	public Dictionary<SystemTracker, bool> System { get; set; } = new();

	public Dictionary<ProcessTracker, bool> Process { get; set; } = new();

	public Dictionary<WriterTracker, bool> Writer { get; set; } = new();

	public Dictionary<EventTracker, bool> Events { get; set; } = new();

	public Dictionary<Cache, bool> CacheHitsMisses { get; set; } = new();

	public bool ProjectionStats { get; set; }

	public bool ProjectionExecution { get; set; }

	public bool ProjectionExecutionByFunction { get; set; }

	public bool PersistentSubscriptionStats { get; set; } = false;

	public bool ElectionsCount { get; set; } = false;

	public bool CacheResources { get; set; } = false;

	// must be 0, 1, 5, 10 or a multiple of 15
	public int ExpectedScrapeIntervalSeconds { get; set; }

	public Dictionary<QueueTracker, bool> Queues { get; set; } = new();

	public LabelMappingCase[] QueueLabels { get; set; } = [];

	public LabelMappingCase[] MessageTypes { get; set; } = [];

	public static ExplicitBucketHistogramConfiguration SecondsHistogramBucketConfiguration { get; set; } =
		new() {
			Boundaries = [
				0.000_001, // 1 microsecond
				0.000_01, 0.000_1, 0.001, // 1 millisecond
				0.01, 0.1, 1, // 1 second
				10,
			]
		};

	public static ExplicitBucketHistogramConfiguration LatencySecondsHistogramBucketConfiguration { get; set; } =
		new() {
			Boundaries = [
				0.001, //    1 ms
				0.005, //    5 ms
				0.01,  //   10 ms
				0.05,  //   50 ms
				0.1,   //  100 ms
				0.5,   //  500 ms
				1,     // 1000 ms
				5,     // 5000 ms
			]
		};
}
