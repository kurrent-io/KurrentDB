using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EventStore.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace EventStore.Common.Configuration {
	public class MetricsConfiguration {
		public static IConfiguration FromFile(string metricsConfig = "metricsconfig.json") {
			var configurationDirectory = Path.IsPathRooted(metricsConfig)
				? Path.GetDirectoryName(metricsConfig)
				: Locations
					.GetPotentialConfigurationDirectories()
					.FirstOrDefault(directory => File.Exists(Path.Combine(directory, metricsConfig)));

			if (configurationDirectory == null) {
				throw new FileNotFoundException(
					$"Could not find {metricsConfig} in the following directories: {string.Join(", ", Locations.GetPotentialConfigurationDirectories())}");
			}

			var configurationRoot = new ConfigurationBuilder()
				.AddJsonFile(config => {
					config.Optional = false;
					config.FileProvider = new PhysicalFileProvider(configurationDirectory);
					config.OnLoadException = context => Serilog.Log.Error(context.Exception, "err");
					config.Path = Path.GetFileName(metricsConfig);
				})
				.Build();

			return configurationRoot;
		}

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
			Busy = 1,
			Length,
			Processing,
		}

		public enum PersistentSubscriptionTracker {
			ParkedMessages = 1,
		}

		public class LabelMappingCase {
			public string Regex { get; set; }
			public string Label { get; set; }
		}

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

		public bool CacheResources { get; set; } = false;

		// must be 0, 1, 5, 10 or a multiple of 15
		public int ExpectedScrapeIntervalSeconds { get; set; }

		public Dictionary<QueueTracker, bool> Queues { get; set; } = new();

		public Dictionary<PersistentSubscriptionTracker, bool> PersistentSubscriptions { get; set; } = new();

		public LabelMappingCase[] QueueLabels { get; set; } = Array.Empty<LabelMappingCase>();

		public LabelMappingCase[] MessageTypes { get; set; } = Array.Empty<LabelMappingCase>();
	}
}
