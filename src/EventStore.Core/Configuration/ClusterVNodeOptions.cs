// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Configuration;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Serilog;
using Quickenshtein;

namespace EventStore.Core;

[PublicAPI]
public partial record ClusterVNodeOptions {
	public ClusterVNodeOptions() => FileStreamExtensions.ConfigureFlush(Database.UnsafeDisableFlushToDisk);

	public IConfigurationRoot? ConfigurationRoot { get; init; }
	[OptionGroup] public ApplicationOptions Application { get; init; } = new();
	[OptionGroup] public DevModeOptions DevMode { get; init; } = new();
	[OptionGroup] public DefaultUserOptions DefaultUser { get; init; } = new();
	[OptionGroup] public LoggingOptions Logging { get; init; } = new();
	[OptionGroup] public AuthOptions Auth { get; init; } = new();
	[OptionGroup] public CertificateOptions Certificate { get; init; } = new();
	[OptionGroup] public CertificateFileOptions CertificateFile { get; init; } = new();
	[OptionGroup] public CertificateStoreOptions CertificateStore { get; init; } = new();
	[OptionGroup] public ClusterOptions Cluster { get; init; } = new();
	[OptionGroup] public DatabaseOptions Database { get; init; } = new();
	[OptionGroup] public GrpcOptions Grpc { get; init; } = new();
	[OptionGroup] public InterfaceOptions Interface { get; init; } = new();
	[OptionGroup] public ProjectionOptions Projection { get; init; } = new();
	public UnknownOptions Unknown { get; init; } = new([]);

	public byte IndexBitnessVersion { get; init; } = Index.PTableVersions.IndexV4;

	public X509Certificate2? ServerCertificate { get; init; }
	public X509Certificate2Collection? TrustedRootCertificates { get; init; }
	public IReadOnlyList<IPlugableComponent> PlugableComponents { get; init; } = [];

	public IReadOnlyList<ISubsystem> Subsystems => PlugableComponents.OfType<ISubsystem>().ToArray();

	public bool UnknownOptionsDetected => Unknown.Options.Any();

	public static ClusterVNodeOptions FromConfiguration(IConfigurationRoot configurationRoot) {
		var configuration = configurationRoot.GetRequiredSection(KurrentConfigurationKeys.Prefix);

		// required because of a bug in the configuration system that
		// is not reading the attribute from the property itself
		TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
		TypeDescriptor.AddAttributes(typeof(EndPoint), new TypeConverterAttribute(typeof(GossipEndPointConverter)));
		TypeDescriptor.AddAttributes(typeof(IPAddress), new TypeConverterAttribute(typeof(IPAddressConverter)));

		// with full keys we would not even need to do all these binds, just a single one
		// configurationRoot.BindOptions<ClusterVNodeOptions>();

		var options = new ClusterVNodeOptions {
			Application = configuration.BindOptions<ApplicationOptions>(),
			DevMode = configuration.BindOptions<DevModeOptions>(),
			DefaultUser = configuration.BindOptions<DefaultUserOptions>(),
			Logging = configuration.BindOptions<LoggingOptions>(),
			Auth = configuration.BindOptions<AuthOptions>(),
			Certificate = configuration.BindOptions<CertificateOptions>(),
			CertificateFile = configuration.BindOptions<CertificateFileOptions>(),
			CertificateStore = configuration.BindOptions<CertificateStoreOptions>(),
			Cluster = configuration.BindOptions<ClusterOptions>(),
			Database = configuration.BindOptions<DatabaseOptions>(),
			Grpc = configuration.BindOptions<GrpcOptions>(),
			Interface = configuration.BindOptions<InterfaceOptions>(),
			Projection = configuration.BindOptions<ProjectionOptions>(),

			Unknown = UnknownOptions.FromConfiguration(configuration),
			ConfigurationRoot = configurationRoot,
			LoadedOptions = GetLoadedOptions(configurationRoot)
		};

		return options;
	}

	[Description("Default User Options")]
	public record DefaultUserOptions {
		[Description("Admin Default password"), Sensitive, EnvironmentOnly("The Admin user password can only be set using Environment Variables")]
		public string DefaultAdminPassword { get; init; } = "changeit";

		[Description("Ops Default password"), Sensitive, EnvironmentOnly("The Ops user password can only be set using Environment Variables")]
		public string DefaultOpsPassword { get; init; } = "changeit";
	}

	[Description("Dev Mode Options")]
	public record DevModeOptions {
		[Description("Runs KurrentDB in dev mode. This will create and add dev certificates to your certificate store, enable atompub over http, and run standard projections.")]
		public bool Dev { get; init; } = false;

		[Description("Removes any dev certificates installed on this computer without starting KurrentDB.")]
		public bool RemoveDevCerts { get; init; } = false;
	}

	[Description("Application Options")]
	public record ApplicationOptions {
		[Description("Show help.")] public bool Help { get; init; } = false;

		[Description("Show version.")] public bool Version { get; init; } = false;

		[Description("Configuration files.")]
		public string Config { get; init; } = DefaultFiles.DefaultConfigPath;

		[Description("Print effective configuration to console and then exit.")]
		public bool WhatIf { get; init; } = false;

		[Description("Allows KurrentDB to run with unknown configuration options present.")]
		public bool AllowUnknownOptions { get; init; } = false;

		[Description("Disable HTTP caching.")] public bool DisableHttpCaching { get; init; } = false;

		[Description("The number of seconds between statistics gathers."),
		 Unit("s")]
		public int StatsPeriodSec { get; init; } = 30;

		[Description("The number of threads to use for pool of worker services. Set to '0' to scale automatically (Default)")]
		public int WorkerThreads { get; init; } = 0;

		[Description("Enables the tracking of various histograms in the backend, " +
		             "typically only used for debugging, etc.")]
		[Deprecated("The EnableHistograms setting has been deprecated as of version 24.10.0 and currently has no effect. " +
					"Please contact Kurrent if this feature is of interest to you.")]
		public bool EnableHistograms { get; init; } = false;

		[Description("Log Http Requests and Responses before processing them.")]
		public bool LogHttpRequests { get; init; } = false;

		[Description("Log the failed authentication attempts.")]
		public bool LogFailedAuthenticationAttempts { get; init; } = false;

		[Description("Skip Index Scan on Reads. This skips the index scan which was used " +
		             "to stop reading duplicates.")]
		public bool SkipIndexScanOnReads { get; init; } = false;

		[Description("The maximum size of appends, in bytes. This is the total size of all events in the append request. " +
		             "May not exceed 16MB.")]
		public int MaxAppendSize { get; init; } = 1_024 * 1_024;

		[Description("The maximum size of an individual event in an append request received over gRPC or HTTP, in bytes.")]
		public int MaxAppendEventSize { get; init; } = int.MaxValue;

		[Description("Disable Authentication, Authorization and TLS on all TCP/HTTP interfaces.")]
		public bool Insecure { get; init; } = false;

		[Description("Allow anonymous access to HTTP API endpoints.")]
		public bool AllowAnonymousEndpointAccess { get; init; } = false;

		[Description("Allow anonymous access to streams.")]
		public bool AllowAnonymousStreamAccess { get; init; } = false;

		[Description("Overrides anonymous access for the gossip endpoint. If set to true, the gossip endpoint will accept anonymous access. " +
		             $"Otherwise anonymous access will be dis/allowed based on the value of the '{nameof(AllowAnonymousEndpointAccess)}' option")]
		public bool OverrideAnonymousEndpointAccessForGossip { get; init; } = true;

		[Description("Disable telemetry data collection."), EnvironmentOnly("You can only opt-out of telemetry using Environment Variables")]
		public bool TelemetryOptout { get; init; } = false;
	}

	[Description("Logging Options")]
	public record LoggingOptions {
		[Description("Path where to keep log files.")]
		public string Log { get; init; } = Locations.DefaultLogDirectory;

		[Description("The name of the log configuration file.")]
		public string LogConfig { get; init; } = "logconfig.json";

		[Description("Sets the minimum log level. For more granular settings, please edit logconfig.json.")]
		public LogLevel LogLevel { get; init; } = LogLevel.Default;

		[Description("Which format (plain, json) to use when writing to the console.")]
		public LogConsoleFormat LogConsoleFormat { get; init; } = LogConsoleFormat.Plain;

		[Description("Maximum size of each log file.")]
		public int LogFileSize { get; init; } = 1024 * 1024 * 1024;

		[Description("How often to rotate logs.")]
		public RollingInterval LogFileInterval { get; init; } = RollingInterval.Day;

		[Description("How many log files to hold on to.")]
		public int LogFileRetentionCount { get; init; } = 31;

		[Description("Disable log to disk.")]
		public bool DisableLogFile { get; init; } = false;
	}

	[Description("Authentication/Authorization Options")]
	public record AuthOptions {
		[Description("The type of authorization to use.")]
		public string AuthorizationType { get; init; } = "internal";

		[Description("Path to the configuration file for authorization configuration (if applicable).")]
		public string? AuthorizationConfig { get; init; }

		[Description("The type of Authentication to use.")]
		public string AuthenticationType { get; init; } = "internal";

		[Description("Path to the configuration file for Authentication configuration (if applicable).")]
		public string? AuthenticationConfig { get; init; }

		[Description("Disables first level authorization checks on all HTTP endpoints. " +
		             "This option can be enabled for backwards compatibility with EventStore 5.0.1 or earlier.")]
		public bool DisableFirstLevelHttpAuthorization { get; init; } = false;
	}

	[Description("Certificate Options (from file)")]
	public record CertificateFileOptions {
		[Description("The path to a PKCS #12 (.p12/.pfx) or an X.509 (.pem, .crt, .cer, .der) certificate file. " +
		             "If you have intermediate certificates, they should be bundled together in a PEM or PKCS #12 file containing the node's certificate followed by the intermediate certificates.")]
		public string? CertificateFile { get; init; }

		[Description("The path to the certificate private key file (.key) if an X.509 (.pem, .crt, .cer, .der) " +
		             "certificate file is provided.")]
		public string? CertificatePrivateKeyFile { get; init; }

		[Description("The password to the certificate if a PKCS #12 (.p12/.pfx) certificate file is provided."),
		 Sensitive]
		public string? CertificatePassword { get; init; }

		[Description("The password to the certificate private key file if an encrypted PKCS #8 private key file is provided."),
		 Sensitive]
		public string? CertificatePrivateKeyPassword { get; init; }
	}

	[Description("Certificate Options")]
	public record CertificateOptions {
		[Description("The path to a directory which contains trusted X.509 (.pem, .crt, .cer, .der) " +
		             "root certificate files.")]
		public string? TrustedRootCertificatesPath { get; init; } =
			Locations.DefaultTrustedRootCertificateDirectory;

		[Description("The pattern the CN (Common Name) of a connecting KurrentDB node must match to be authenticated. A wildcard FQDN can be specified if using wildcard certificates or if the CN is not the same on all nodes. Leave empty to automatically use the CN of this node's certificate.")]
		public string CertificateReservedNodeCommonName { get; init; } = string.Empty;
	}

	[Description("Certificate Options (from store)")]
	public record CertificateStoreOptions {
		[Description("The certificate store location name.")]
		public string CertificateStoreLocation { get; init; } = string.Empty;

		[Description("The certificate store name.")]
		public string CertificateStoreName { get; init; } = string.Empty;

		[Description("The certificate store subject name.")]
		public string CertificateSubjectName { get; init; } = string.Empty;

		[Description("The certificate fingerprint/thumbprint.")]
		public string CertificateThumbprint { get; init; } = string.Empty;

		[Description("The name of the certificate store that contains the trusted root certificate.")]
		public string TrustedRootCertificateStoreName { get; init; } = string.Empty;

		[Description("The certificate store location that contains the trusted root certificate.")]
		public string TrustedRootCertificateStoreLocation { get; init; } = string.Empty;

		[Description("The trusted root certificate subject name.")]
		public string TrustedRootCertificateSubjectName { get; init; } = string.Empty;

		[Description("The trusted root certificate fingerprint/thumbprint.")]
		public string TrustedRootCertificateThumbprint { get; init; } = string.Empty;
	}

	[Description("Cluster Options")]
	public record ClusterOptions {
		[Description("The maximum number of entries to keep in the stream info cache. Set to '0' to scale automatically (Default)")]
		public int StreamInfoCacheCapacity { get; init; } = 0;

		[Description("The number of nodes in the cluster.")]
		public int ClusterSize { get; init; } = 1;

		[Description("The node priority used during leader election.")]
		public int NodePriority { get; init; } = 0;

		[Description("Whether to use DNS lookup to discover other cluster nodes.")]
		public bool DiscoverViaDns { get; init; } = true;

		[Description("DNS name from which other nodes can be discovered.")]
		public string ClusterDns { get; init; } = "fake.dns";

		[Description("The port on which cluster nodes' managers are running.")]
		public int ClusterGossipPort { get; init; } = 2113;

		[Description("Endpoints for other cluster nodes from which to seed gossip.")]
		public EndPoint[] GossipSeed { get; init; } = [];

		[Description("The interval, in ms, nodes should try to gossip with each other."),
		 Unit("ms")]
		public int GossipIntervalMs { get; init; } = 2_000;

		[Description("The amount of drift, in ms, between clocks on nodes allowed before gossip is rejected."),
		 Unit("ms")]
		public int GossipAllowedDifferenceMs { get; init; } = 60_000;

		[Description("The timeout, in ms, on gossip to another node."),
		 Unit("ms")]
		public int GossipTimeoutMs { get; init; } = 2_500;

		[Description("Sets this node as a read only replica that is not allowed to participate in elections " +
		             "or accept writes from clients.")]
		public bool ReadOnlyReplica { get; init; } = false;

		[Description("Sets this node as an Archiver node. Requires ReadOnlyReplica to be true. Experimental.")]
		public bool Archiver { get; init; } = false;

		[Description("Allow more nodes than the cluster size to join the cluster as clones. " +
		             "(UNSAFE: can cause data loss if a clone is promoted as leader)")]
		public bool UnsafeAllowSurplusNodes { get; init; } = false;

		[Description("The number of seconds a dead node will remain in the gossip before being pruned."),
		 Unit("s")]
		public int DeadMemberRemovalPeriodSec { get; init; } = 1_800;

		[Description("The timeout, in milliseconds, on election messages to other nodes."),
		 Unit("ms")]
		public int LeaderElectionTimeoutMs { get; init; } = 1_000;

		public int QuorumSize => ClusterSize == 1 ? 1 : ClusterSize / 2 + 1;
	}

	[Description("Database Options")]
	public record DatabaseOptions {
		[Description("The minimum flush delay in milliseconds."),
		 Unit("ms")]
		public double MinFlushDelayMs { get; init; } = TFConsts.MinFlushDelayMs.TotalMilliseconds;

		[Description("Disables the merging of chunks when scavenge is running.")]
		public bool DisableScavengeMerging { get; init; } = false;

		[Description("The number of days to keep scavenge history."),
		Unit("d")]
		public int ScavengeHistoryMaxAge { get; init; } = 30;

		[Description("The number of chunks to cache in unmanaged memory.")]
		public int CachedChunks { get; init; } = -1;

		[Description("The amount of unmanaged memory to use for caching chunks in bytes.")]
		public long ChunksCacheSize { get; init; } = TFConsts.ChunksCacheSize;

		[Description("Adjusts the maximum size of a mem table.")]
		public int MaxMemTableSize { get; init; } = 1_000_000;

		[Description("The number of events to read per candidate in the case of a hash collision.")]
		public int HashCollisionReadLimit { get; init; } = 100;

		[Description("The path the db should be loaded/saved to.")]
		public string Db { get; init; } = Locations.DefaultDataDirectory;

		[Description("The path the index should be loaded/saved to.")]
		public string? Index { get; init; } = null;

		[Description("The type of transformation to apply to the database.")]
		public string Transform { get; init; } = "identity";

		[Description("Keep everything in memory, no directories or files are created.")]
		public bool MemDb { get; init; } = false;

		[Description("Creates a Bloom filter file for each new index file to speed up index reads.")]
		public bool UseIndexBloomFilters { get; init; } = true;

		[Description("The maximum number of entries to keep in each index cache.")]
		public int IndexCacheSize { get; init; } = 0;

		[Description("Bypasses the checking of file hashes of database during startup " +
		             "(allows for faster startup).")]
		public bool SkipDbVerify { get; init; } = false;

		[Description("Enables Write Through when writing to the file system, this bypasses filesystem caches.")]
		public bool WriteThrough { get; init; } = false;

		[Description("Enables Unbuffered/DirectIO when writing to the file system, this bypasses filesystem " +
		             "caches.")]
		[Deprecated("The Unbuffered setting has been deprecated as of version 24.6.0 and currently has no effect. " +
		            "Please contact Kurrent if this feature is of interest to you.")]
		public bool Unbuffered { get; init; } = false;

		[Description("The initial number of readers to start when opening a TFChunk.")]
		[Deprecated("The ChunkInitialReaderCount parameter has been deprecated as of version 24.6.0 and currently has no effect.")]
		public int ChunkInitialReaderCount { get; init; } = 5;

		[Description("Prepare timeout (in milliseconds)."),
		 Unit("ms")]
		public int PrepareTimeoutMs { get; init; } = 2_000;

		[Description("Commit timeout (in milliseconds)."),
		 Unit("ms")]
		public int CommitTimeoutMs { get; init; } = 2_000;

		[Description("Write timeout (in milliseconds)."),
		 Unit("ms")]
		public int WriteTimeoutMs { get; init; } = 2_000;

		[Description("Disable flushing to disk. (UNSAFE: on power off)")]
		public bool UnsafeDisableFlushToDisk { get; init; }

		[Description("Disables Hard Deletes. (UNSAFE: use to remove hard deletes)")]
		[Deprecated("This setting is unsafe and not recommended")]
		public bool UnsafeIgnoreHardDelete { get; init; } = false;

		[Description("Bypasses the checking of file hashes of indexes during startup and after index merges " +
		             "(allows for faster startup and less disk pressure after merges).")]
		public bool SkipIndexVerify { get; init; } = false;

		[Description("Sets the depth to cache for the mid point cache in index.")]
		public int IndexCacheDepth { get; init; } = 16;

		[Description("Change the way the DB files are opened to reduce their stickiness in the system file cache.")]
		public bool ReduceFileCachePressure { get; init; } = false;

		[Description("Number of threads to be used to initialize the database. " +
		             "Will be capped at host processor count.")]
		public int InitializationThreads { get; init; } = 1;

		[Description("The number of reader threads to use for processing reads. Set to '0' to scale automatically (Default)")]
		public int ReaderThreadsCount { get; init; } = 0;

		[Description("During large Index Merge operations, writes may be slowed down. Set this to the maximum " +
		             "index file level for which automatic merges should happen. Merging indexes above this level " +
		             "should be done manually.")]
		public int MaxAutoMergeIndexLevel { get; init; } = int.MaxValue;

		[Description("Set this option to write statistics to the database.")]
		public bool WriteStatsToDb {
			get => (StatsStorage.Stream & StatsStorage) != 0;
			init => StatsStorage =
				value
					? StatsStorage.StreamAndFile
					: StatsStorage.File; // TODO SS: not sure if we should do this here
		}

		[Description("When truncate.chk is set, the database will be truncated on startup. " +
		             "This is a safety check to ensure large amounts of data truncation does not happen " +
		             "accidentally. This value should be set in the low 10,000s for allow for " +
		             "standard cluster recovery operations. -1 is no max.")]
		public long MaxTruncation { get; init; } = 256 * 1_024 * 1_024;

		public int ChunkSize { get; init; } = TFConsts.ChunkSize;

		public StatsStorage StatsStorage { get; init; } = StatsStorage.File;

		[Description("The log format version to use for storing the event log. " +
		             "V3 is currently in development and should only be used for testing purposes.")]
		public DbLogFormat DbLogFormat { get; init; } = DbLogFormat.V2;

		[Description("The amount of memory & disk space, in bytes, to use for the stream existence filter. " +
		             "This should be set to roughly the maximum number of streams you expect to have in your database, " +
		             "i.e if you expect to have a max of 500 million streams, use a value of 500 megabytes. " +
		             "The value you select should also fit entirely in memory to avoid any performance degradation. " +
		             "Use 0 to disable the filter. Resizing the filter will cause a full rebuild.")]
		public long StreamExistenceFilterSize { get; init; } = Opts.StreamExistenceFilterSizeDefault;

		[Description("The page size of the scavenge database.")]
		public int ScavengeBackendPageSize { get; init; } = Opts.ScavengeBackendPageSizeDefault;

		[Description("The amount of memory to use for backend caching in bytes.")]
		public long ScavengeBackendCacheSize { get; init; } = Opts.ScavengeBackendCacheSizeDefault;

		[Description("The number of stream hashes to remember when checking for collisions.")]
		public int ScavengeHashUsersCacheCapacity { get; init; } = Opts.ScavengeHashUsersCacheCapacityDefault;
	}

	[Description("gRPC Options")]
	public record GrpcOptions {
		[Description("Controls the period (in milliseconds) after which a keepalive ping " +
		             "is sent on the transport."),
		 Unit("ms")]
		public int KeepAliveInterval { get; init; } = 10_000;

		[Description("Controls the amount of time (in milliseconds) the sender of the keepalive ping waits " +
		             "for an acknowledgement. If it does not receive an acknowledgment within this time, " +
		             "it will close the connection."),
		 Unit("ms")]
		public int KeepAliveTimeout { get; init; } = 10_000;

		internal static GrpcOptions FromConfiguration(IConfiguration configurationRoot) => new() {
			KeepAliveInterval = configurationRoot.GetValue<int>(nameof(KeepAliveInterval)),
			KeepAliveTimeout = configurationRoot.GetValue<int>(nameof(KeepAliveTimeout))
		};
	}

	[Description("Interface Options")]
	public record InterfaceOptions {
		[Description("The IP Address used by internal replication between nodes in the cluster.")]
		public IPAddress ReplicationIp { get; init; } = IPAddress.Loopback;

		[Description("The IP Address for the node.")]
		public IPAddress NodeIp { get; init; } = IPAddress.Loopback;

		[Description("The Port to run the HTTP server on.")]
		public int NodePort { get; init; } = 2113;

		[Description("The TCP port used by internal replication between nodes in the cluster.")]
		public int ReplicationPort { get; init; } = 1112;

		[Description("Advertise the Node's host name to other nodes and external clients as.")]
		public string? NodeHostAdvertiseAs { get; init; } = null;

		[Description("Advertise the Replication host name to other nodes in the cluster as.")]
		public string? ReplicationHostAdvertiseAs { get; init; } = null;

		[Description("Advertise Host in Gossip to Client As.")]
		public string? AdvertiseHostToClientAs { get; init; } = null;

		[Description("Advertise Node Port in Gossip to Client As.")]
		public int AdvertiseNodePortToClientAs { get; init; } = 0;

		[Description("Advertise Http Port As.")]
		public int NodePortAdvertiseAs { get; init; } = 0;

		[Description("Advertise Replication Tcp Port As.")]
		public int ReplicationTcpPortAdvertiseAs { get; init; } = 0;

		[Description("Heartbeat timeout for Replication TCP sockets."),
		 Unit("ms")]
		public int ReplicationHeartbeatTimeout { get; init; } = 700;

		[Description("Heartbeat interval for Replication TCP sockets."),
		 Unit("ms")]
		public int ReplicationHeartbeatInterval { get; init; } = 700;

		[Description("Whether to allow local connections via a UNIX domain socket.")]
		public bool EnableUnixSocket { get; init; } = false;

		[Description("The maximum number of pending send bytes allowed before a connection is closed.")]
		public int ConnectionPendingSendBytesThreshold { get; init; } = 10 * 1_024 * 1_024;

		[Description("The maximum number of pending connection operations allowed before a connection is closed.")]
		public int ConnectionQueueSizeThreshold { get; init; } = 50_000;

		[Description("Disables the admin ui on the HTTP endpoint.")]
		public bool DisableAdminUi { get; init; } = false;

		[Description("Disables statistics requests on the HTTP endpoint.")]
		public bool DisableStatsOnHttp { get; init; } = false;

		[Description("Disables gossip requests on the HTTP endpoint.")]
		public bool DisableGossipOnHttp { get; init; } = false;

		[Description("Enables trusted authentication by an intermediary in the HTTP.")]
		public bool EnableTrustedAuth { get; init; } = false;

		[Description("Enable AtomPub over HTTP Interface."),
		 Deprecated("AtomPub over HTTP Interface has been deprecated as of version 20.6.0. It is recommended to use gRPC instead")]
		public bool EnableAtomPubOverHttp { get; init; } = false;
	}

	[Description("Projection Options")]
	public record ProjectionOptions {
		public const int DefaultProjectionExecutionTimeout = 250;
		[Description("Enables the running of projections. System runs built-in projections, " +
		             "All runs user projections.")]
		public ProjectionType RunProjections { get; init; }

		[Description("Start the built in system projections.")]
		public bool StartStandardProjections { get; init; } = false;

		[Description("The number of threads to use for projections.")]
		public int ProjectionThreads { get; init; } = 3;

		[Description("The number of minutes a query can be idle before it expires."),
		 Unit("m")]
		public int ProjectionsQueryExpiry { get; init; } = 5;

		[Description("Fault the projection if the Event number that was expected in the stream differs " +
		             "from what is received. This may happen if events have been deleted or expired.")]
		public bool FaultOutOfOrderProjections { get; init; } = false;

		[Description("The time in milliseconds allowed for the compilation phase of user projections"),
		 Unit("ms")]
		public int ProjectionCompilationTimeout { get; set; } = 500;

		[Description("The maximum execution time in milliseconds for executing a handler in a user projection. It can be overridden for a specific projection by setting ProjectionExecutionTimeout config for that projection"),
		 Unit("ms")]
		public int ProjectionExecutionTimeout { get; set; } = DefaultProjectionExecutionTimeout;

		[Description("The maximum size, in bytes, of a projection's state and result. A projection will fault if its state size exceeds this value. May not exceed 16mb.")]
		public int MaxProjectionStateSize { get; set; } = Opts.MaxProjectionStateSizeDefault;
	}

	public record UnknownOptions(IReadOnlyList<(string, string)> Options) {
		/// <summary>
		/// Identifies unknown options in the configuration and provides suggestions for known options.
		/// </summary>
		public static UnknownOptions FromConfiguration(IConfiguration configuration) {
			var knownKeys = Metadata
				.SelectMany(x => x.Options)
				.Select(x => x.Key)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var unknownKeys = FindUnknownKeys(configuration, knownKeys);

			var result = unknownKeys
				.Select(unknownKey => CreateUnknownOptionResult(knownKeys, unknownKey))
				.ToList();

			return new(result);

			static IEnumerable<string> FindUnknownKeys(IConfiguration configuration,
				IReadOnlySet<string> knownKeys) {
				var unknownKeys = configuration
					.AsEnumerable()
					.Select(kvp => kvp.Key)
					.Where(key => key != KurrentConfigurationKeys.Prefix
					              && !knownKeys.Contains(KurrentConfigurationKeys.Normalize(key)))
					.ToList();

				var unknownSections = FindUnknownSections(unknownKeys);

				// only report top level unknown keys. plugins, metrics, etc will use nested keys.
				// in the future we may report unknown keys in nested sections but it is out of scope for now.
				return unknownKeys
					.Where(key => !unknownSections.Any(key.StartsWith));
			}

			static HashSet<string> FindUnknownSections(IEnumerable<string> keys) {
				// if it has more than 2 sections, we found a value with an unknown section
				var hashSet = new HashSet<string>();

				foreach (var key in keys.Where(key => key.Split(":").Length > 2))
					hashSet.Add(key[..key.LastIndexOf(':')]);

				return hashSet;
			}

			static (string UnknownKey, string SuggestedKey) CreateUnknownOptionResult(IEnumerable<string> knownKeys,
				string unknownKey, int distanceThreshold = 5) {
				var suggestion = knownKeys
					.Select(key => (AllowedKey: key, Distance: Levenshtein.GetDistance(unknownKey, key)))
					.MinBy(x => x.Distance);

				return (
					UnknownKey: KurrentConfigurationKeys.StripConfigurationPrefix(unknownKey),
					SuggestedKey: suggestion.Distance > distanceThreshold
						? ""
						: KurrentConfigurationKeys.StripConfigurationPrefix(suggestion.AllowedKey)
				);
			}
		}
	}
}
