using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common;
using EventStore.Common.Configuration;
using EventStore.Common.Exceptions;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Util;
using Microsoft.Extensions.Configuration;
using Serilog;
using Quickenshtein;

#nullable enable
namespace EventStore.Core {
	public partial record ClusterVNodeOptions {
		public static readonly ClusterVNodeOptions Default = new();

		internal IConfigurationRoot? ConfigurationRoot { get; init; }
		[OptionGroup] public ApplicationOptions Application { get; init; } = new();
		[OptionGroup] public DevModeOptions DevMode { get; init; } = new();
		[OptionGroup] public DefaultUserOptions DefaultUser { get; init; } = new();
		[OptionGroup] public LoggingOptions Log { get; init; } = new();
		[OptionGroup] public AuthOptions Auth { get; init; } = new();
		[OptionGroup] public CertificateOptions Certificate { get; init; } = new();
		[OptionGroup] public CertificateFileOptions CertificateFile { get; init; } = new();
		[OptionGroup] public CertificateStoreOptions CertificateStore { get; init; } = new();
		[OptionGroup] public ClusterOptions Cluster { get; init; } = new();
		[OptionGroup] public DatabaseOptions Database { get; init; } = new();
		[OptionGroup] public GrpcOptions Grpc { get; init; } = new();
		[OptionGroup] public InterfaceOptions Interface { get; init; } = new();
		[OptionGroup] public ProjectionOptions Projections { get; init; } = new();
		public UnknownOptions Unknown { get; init; } = new(Array.Empty<(string, string)>());

		public byte IndexBitnessVersion { get; init; } = Index.PTableVersions.IndexV4;

		public IReadOnlyList<ISubsystemFactory> Subsystems { get; init; } = Array.Empty<ISubsystemFactory>();

		public ClusterVNodeOptions WithSubsystem(ISubsystemFactory subsystem) => this with {
			Subsystems = new List<ISubsystemFactory>(Subsystems) { subsystem }
		};

		public X509Certificate2? ServerCertificate { get; init; }

		public X509Certificate2Collection? TrustedRootCertificates { get; init; }

		internal string? DebugView => ConfigurationRoot?.GetDebugView();

		public static ClusterVNodeOptions FromConfiguration(string[] args, IDictionary environment) {
			if (args == null) throw new ArgumentNullException(nameof(args));
			if (environment == null) throw new ArgumentNullException(nameof(environment));
			
			try {
				var configurationRoot = new ConfigurationBuilder()
					.AddEventStore(nameof(ApplicationOptions.Config), args, environment, DefaultValues)
					.Build();
				return FromConfiguration(configurationRoot);
			} catch (Exception e) {
				throw new InvalidConfigurationException(e.Message);
			}
		}

		public static ClusterVNodeOptions FromConfiguration(IConfigurationRoot configurationRoot) {
			return new ClusterVNodeOptions {
				Application = ApplicationOptions.FromConfiguration(configurationRoot),
				DevMode = DevModeOptions.FromConfiguration(configurationRoot),
				DefaultUser = DefaultUserOptions.FromConfiguration(configurationRoot),
				Log = LoggingOptions.FromConfiguration(configurationRoot),
				Auth = AuthOptions.FromConfiguration(configurationRoot),
				Certificate = CertificateOptions.FromConfiguration(configurationRoot),
				CertificateFile = CertificateFileOptions.FromConfiguration(configurationRoot),
				CertificateStore = CertificateStoreOptions.FromConfiguration(configurationRoot),
				Cluster = ClusterOptions.FromConfiguration(configurationRoot),
				Database = DatabaseOptions.FromConfiguration(configurationRoot),
				Grpc = GrpcOptions.FromConfiguration(configurationRoot),
				Interface = InterfaceOptions.FromConfiguration(configurationRoot),
				Projections = ProjectionOptions.FromConfiguration(configurationRoot),
				Unknown = UnknownOptions.FromConfiguration(configurationRoot),
				ConfigurationRoot = configurationRoot,
			};
		}

		public ClusterVNodeOptions Reload() => ConfigurationRoot == null ? this : FromConfiguration(ConfigurationRoot);

		[Description("Default User Options")]
		public record DefaultUserOptions {
			
			[Description("Admin Default password"), Sensitive, EnvironmentOnly("The Admin user password can only be set using Environment Variables")]
			public string DefaultAdminPassword { get; init; } = "changeit";
			
			[Description("Ops Default password"), Sensitive, EnvironmentOnly("The Ops user password can only be set using Environment Variables")] 
			public string DefaultOpsPassword { get; init; } = "changeit";
 
			internal static DefaultUserOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				DefaultAdminPassword = configurationRoot.GetString(nameof(DefaultAdminPassword)),
				DefaultOpsPassword = configurationRoot.GetString(nameof(DefaultOpsPassword))
			};
		}
		
		[Description("Dev mode Options")]
		public record DevModeOptions {
			[Description("Runs EventStoreDB in dev mode. This will create and add dev certificates to your certificate store, enable atompub over http, and run standard projections.")]
			public bool Dev { get; init; } = false;

			[Description("Removes any dev certificates installed on this computer without starting EventStoreDB.")]
			public bool RemoveDevCerts { get; init; } = false;

			internal static DevModeOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				Dev = configurationRoot.GetValue<bool>(nameof(Dev)),
				RemoveDevCerts = configurationRoot.GetValue<bool>(nameof(RemoveDevCerts))
			};
		}

		[Description("Application Options")]
		public record ApplicationOptions {
			[Description("Show help.")] public bool Help { get; init; } = false;
			
			[Description("Show version.")] public bool Version { get; init; } = false;

			[Description("Configuration files.")]
			public string Config { get; init; } =
				Path.Combine(Locations.DefaultConfigurationDirectory, DefaultFiles.DefaultConfigFile);

			[Description("Print effective configuration to console and then exit.")]
			public bool WhatIf { get; init; } = false;

			[Description("Allows EventStoreDB to run with unknown configuration options present.")]
			public bool AllowUnknownOptions { get; init; } = false;

			[Description("Disable HTTP caching.")] public bool DisableHttpCaching { get; init; } = false;

			[Description("The number of seconds between statistics gathers.")]
			public int StatsPeriodSec { get; init; } = 30;

			[Description("The number of threads to use for pool of worker services. Set to '0' to scale automatically (Default)")]
			public int WorkerThreads { get; init; } = 0;

			[Description("Enables the tracking of various histograms in the backend, " +
			             "typically only used for debugging, etc.")]
			public bool EnableHistograms { get; init; } = false;

			[Description("Log Http Requests and Responses before processing them.")]
			public bool LogHttpRequests { get; init; } = false;

			[Description("Log the failed authentication attempts.")]
			public bool LogFailedAuthenticationAttempts { get; init; } = false;

			[Description("Skip Index Scan on Reads. This skips the index scan which was used " +
			             "to stop reading duplicates.")]
			public bool SkipIndexScanOnReads { get; init; } = false;

			[Description("The maximum size of appends, in bytes. May not exceed 16MB.")]
			public int MaxAppendSize { get; init; } = 1_024 * 1_024;

			[Description("Disable Authentication, Authorization and TLS on all TCP/HTTP interfaces.")]
			public bool Insecure { get; init; } = false;

			[Description("Allow anonymous access to HTTP API endpoints.")]
			public bool AllowAnonymousEndpointAccess { get; init; } = false;

			[Description("Allow anonymous access to streams.")]
			public bool AllowAnonymousStreamAccess { get; init; } = false;

			[Description("Overrides anonymous access for the gossip enpdoint. If set to true, the gossip endpoint will accept anonymous access. " +
			             $"Otherwise anonymous access wil be dis/allowed based on the value of the '{nameof(AllowAnonymousEndpointAccess)}' option")]
			public bool OverrideAnonymousEndpointAccessForGossip { get; init; } = true;

			[Description("Disable telemetry data collection."), EnvironmentOnly("You can only opt-out of telemetry using Environment Variables")]
			public bool TelemetryOptout { get; init; } = false;

			internal static ApplicationOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				Config = configurationRoot.GetString(nameof(Config)),
				Help = configurationRoot.GetValue<bool>(nameof(Help)),
				Version = configurationRoot.GetValue<bool>(nameof(Version)),
				EnableHistograms = configurationRoot.GetValue<bool>(nameof(EnableHistograms)),
				WhatIf = configurationRoot.GetValue<bool>(nameof(WhatIf)),
				AllowUnknownOptions = configurationRoot.GetValue<bool>(nameof(AllowUnknownOptions)),
				WorkerThreads = configurationRoot.GetValue<int>(nameof(WorkerThreads)),
				DisableHttpCaching = configurationRoot.GetValue<bool>(nameof(DisableHttpCaching)),
				LogHttpRequests = configurationRoot.GetValue<bool>(nameof(LogHttpRequests)),
				MaxAppendSize = configurationRoot.GetValue<int>(nameof(MaxAppendSize)),
				StatsPeriodSec = configurationRoot.GetValue<int>(nameof(StatsPeriodSec)),
				LogFailedAuthenticationAttempts =
					configurationRoot.GetValue<bool>(nameof(LogFailedAuthenticationAttempts)),
				SkipIndexScanOnReads = configurationRoot.GetValue<bool>(nameof(SkipIndexScanOnReads)),
				Insecure = configurationRoot.GetValue<bool>(nameof(Insecure)),
				AllowAnonymousEndpointAccess = configurationRoot.GetValue<bool>(key:nameof(AllowAnonymousEndpointAccess)),
				AllowAnonymousStreamAccess = configurationRoot.GetValue<bool>(key:nameof(AllowAnonymousStreamAccess)),
				OverrideAnonymousEndpointAccessForGossip = configurationRoot.GetValue<bool>(key:nameof(OverrideAnonymousEndpointAccessForGossip)),
				TelemetryOptout = configurationRoot.GetValue<bool>(key:nameof(TelemetryOptout))
			};
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

			public static LoggingOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				Log = configurationRoot.GetString(nameof(Log)),
				LogConfig = configurationRoot.GetString(nameof(LogConfig)),
				LogLevel = configurationRoot.GetValue<LogLevel>(nameof(LogLevel)),
				LogConsoleFormat = configurationRoot.GetValue<LogConsoleFormat>(nameof(LogConsoleFormat)),
				LogFileSize = configurationRoot.GetValue<int>(nameof(LogFileSize)),
				LogFileInterval = configurationRoot.GetValue<RollingInterval>(nameof(LogFileInterval)),
				LogFileRetentionCount = configurationRoot.GetValue<int>(nameof(LogFileRetentionCount)),
				DisableLogFile = configurationRoot.GetValue<bool>(nameof(DisableLogFile))
			};
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

			internal static AuthOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				AuthorizationType = configurationRoot.GetString(nameof(AuthorizationType)),
				AuthenticationConfig = configurationRoot.GetValue<string>(nameof(AuthenticationConfig)),
				AuthenticationType = configurationRoot.GetString(nameof(AuthenticationType)),
				AuthorizationConfig = configurationRoot.GetValue<string>(nameof(AuthorizationConfig)),
				DisableFirstLevelHttpAuthorization =
					configurationRoot.GetValue<bool>(nameof(DisableFirstLevelHttpAuthorization))
			};
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

			public static CertificateFileOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				CertificatePassword = configurationRoot.GetValue<string>(nameof(CertificatePassword)),
				CertificatePrivateKeyPassword = configurationRoot.GetValue<string>(nameof(CertificatePrivateKeyPassword)),
				CertificatePrivateKeyFile = configurationRoot.GetValue<string>(nameof(CertificatePrivateKeyFile)),
				CertificateFile = configurationRoot.GetValue<string>(nameof(CertificateFile))
			};
		}

		[Description("Certificate Options")]
		public record CertificateOptions {
			[Description("The path to a directory which contains trusted X.509 (.pem, .crt, .cer, .der) " +
			             "root certificate files.")]
			public string? TrustedRootCertificatesPath { get; init; } =
				Locations.DefaultTrustedRootCertificateDirectory;

			[Description("The pattern the CN (Common Name) of a connecting EventStoreDB node must match to be authenticated. A wildcard FQDN can be specified if using wildcard certificates or if the CN is not the same on all nodes. Leave empty to automatically use the CN of this node's certificate.")]
			public string CertificateReservedNodeCommonName { get; init; } = string.Empty;

			internal static CertificateOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				TrustedRootCertificatesPath = configurationRoot.GetValue<string>(nameof(TrustedRootCertificatesPath)),
				CertificateReservedNodeCommonName = configurationRoot.GetString(nameof(CertificateReservedNodeCommonName))
			};
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

			internal static CertificateStoreOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				CertificateStoreLocation = configurationRoot.GetString(nameof(CertificateStoreLocation)),
				CertificateStoreName = configurationRoot.GetString(nameof(CertificateStoreName)),
				CertificateSubjectName = configurationRoot.GetString(nameof(CertificateSubjectName)),
				CertificateThumbprint = configurationRoot.GetString(nameof(CertificateThumbprint)),
				TrustedRootCertificateStoreLocation = configurationRoot.GetString(nameof(TrustedRootCertificateStoreLocation)),
				TrustedRootCertificateStoreName = configurationRoot.GetString(nameof(TrustedRootCertificateStoreName)),
				TrustedRootCertificateThumbprint = configurationRoot.GetString(nameof(TrustedRootCertificateThumbprint)),
				TrustedRootCertificateSubjectName = configurationRoot.GetString(nameof(TrustedRootCertificateSubjectName))
			};
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
			public EndPoint[] GossipSeed { get; init; } = Array.Empty<EndPoint>();

			[Description("The interval, in ms, nodes should try to gossip with each other.")]
			public int GossipIntervalMs { get; init; } = 2_000;

			[Description("The amount of drift, in ms, between clocks on nodes allowed before gossip is rejected.")]
			public int GossipAllowedDifferenceMs { get; init; } = 60_000;

			[Description("The timeout, in ms, on gossip to another node.")]
			public int GossipTimeoutMs { get; init; } = 2_500;

			[Description("Sets this node as a read only replica that is not allowed to participate in elections " +
			             "or accept writes from clients.")]
			public bool ReadOnlyReplica { get; init; } = false;

			[Description("Allow more nodes than the cluster size to join the cluster as clones. " +
			             "(UNSAFE: can cause data loss if a clone is promoted as leader)")]
			public bool UnsafeAllowSurplusNodes { get; init; } = false;

			[Description("The number of seconds a dead node will remain in the gossip before being pruned.")]
			public int DeadMemberRemovalPeriodSec { get; init; } = 1_800;

			[Description("The timeout, in milliseconds, on election messages to other nodes.")]
			public int LeaderElectionTimeoutMs { get; init; } = 1_000;

			public int QuorumSize => ClusterSize == 1 ? 1 : ClusterSize / 2 + 1;

			internal static ClusterOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				GossipSeed = Array.ConvertAll(configurationRoot.GetCommaSeparatedValueAsArray(nameof(GossipSeed)),
					ParseGossipEndPoint),
				DiscoverViaDns = configurationRoot.GetValue<bool>(nameof(DiscoverViaDns)),
				ClusterSize = configurationRoot.GetValue<int>(nameof(ClusterSize)),
				NodePriority = configurationRoot.GetValue<int>(nameof(NodePriority)),
				ClusterDns = configurationRoot.GetString(nameof(ClusterDns)),
				ClusterGossipPort = configurationRoot.GetValue<int>(nameof(ClusterGossipPort)),
				GossipIntervalMs = configurationRoot.GetValue<int>(nameof(GossipIntervalMs)),
				GossipAllowedDifferenceMs = configurationRoot.GetValue<int>(nameof(GossipAllowedDifferenceMs)),
				GossipTimeoutMs = configurationRoot.GetValue<int>(nameof(GossipTimeoutMs)),
				ReadOnlyReplica = configurationRoot.GetValue<bool>(nameof(ReadOnlyReplica)),
				UnsafeAllowSurplusNodes = configurationRoot.GetValue<bool>(nameof(UnsafeAllowSurplusNodes)),
				DeadMemberRemovalPeriodSec = configurationRoot.GetValue<int>(nameof(DeadMemberRemovalPeriodSec)),
				StreamInfoCacheCapacity = configurationRoot.GetValue<int>(nameof(StreamInfoCacheCapacity)),
				LeaderElectionTimeoutMs = configurationRoot.GetValue<int>(nameof(LeaderElectionTimeoutMs))
			};
		}

		[Description("Database Options")]
		public record DatabaseOptions {
			//qq
			[Description("Enabled Chunk Cache Buffer Pooling (experimental)")]
			public bool ChunkCachePooling { get; init; } = true;

			[Description("The minimum flush delay in milliseconds.")]
			public double MinFlushDelayMs { get; init; } = TFConsts.MinFlushDelayMs.TotalMilliseconds;

			[Description("Disables the merging of chunks when scavenge is running.")]
			public bool DisableScavengeMerging { get; init; } = false;

			[Description("The number of days to keep scavenge history.")]
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
			public bool Unbuffered { get; init; } = false;

			[Description("The initial number of readers to start when opening a TFChunk.")]
			public int ChunkInitialReaderCount { get; init; } = 5;

			[Description("Prepare timeout (in milliseconds).")]
			public int PrepareTimeoutMs { get; init; } = 2_000;

			[Description("Commit timeout (in milliseconds).")]
			public int CommitTimeoutMs { get; init; } = 2_000;

			[Description("Write timeout (in milliseconds).")]
			public int WriteTimeoutMs { get; init; } = 2_000;

			private readonly bool _unsafeDisableFlushToDisk = false;

			[Description("Disable flushing to disk. (UNSAFE: on power off)")]
			public bool UnsafeDisableFlushToDisk {
				get => _unsafeDisableFlushToDisk;
				init {
					_unsafeDisableFlushToDisk = value;
					FileStreamExtensions.ConfigureFlush(value);
				}
			}

			[Description("Disables Hard Deletes. (UNSAFE: use to remove hard deletes)")]
			[Deprecated("This setting is unsafe and not recommended")]
			public bool UnsafeIgnoreHardDelete { get; init; } = false;

			[Description("Bypasses the checking of file hashes of indexes during startup and after index merges " +
			             "(allows for faster startup and less disk pressure after merges).")]
			public bool SkipIndexVerify { get; init; } = false;

			[Description("Sets the depth to cache for the mid point cache in index.")]
			public int IndexCacheDepth { get; init; } = 16;

			[Description("Makes index merges faster and reduces disk pressure during merges.")]
			[Deprecated("This setting is ignored by the new scavenge algorithm and will be removed in future versions.")]
			public bool OptimizeIndexMerge { get; init; } = false;

			[Description("Always keeps the newer chunks from a scavenge operation.")]
			[Deprecated("This setting is ignored by the new scavenge algorithm and will be removed in future versions.")]
			public bool AlwaysKeepScavenged { get; init; } = false;

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
				init => StatsStorage = value ? StatsStorage.StreamAndFile : StatsStorage.File;
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

			public static int GetPTableMaxReaderCount(int readerThreadsCount) {
				var ptableMaxReaderCount = 1 /* StorageWriter */
				                           + 1 /* StorageChaser */
				                           + 1 /* Projections */
				                           + TFChunkScavenger.MaxThreadCount /* Scavenging (1 per thread) */
				                           + 1 /* Redaction */
				                           + 1 /* Subscription LinkTos resolving */
				                           + readerThreadsCount
				                           + 5 /* just in case reserve :) */;
				return Math.Max(ptableMaxReaderCount, ESConsts.PTableInitialReaderCount);
			}


			public static int GetTFChunkMaxReaderCount(int readerThreadsCount, int chunkInitialReaderCount) {
				var tfChunkMaxReaderCount =
					GetPTableMaxReaderCount(readerThreadsCount) +
					2 + /* for caching/uncaching, populating midpoints */
					1 + /* for epoch manager usage of elections/replica service */
					1 /* for epoch manager usage of leader replication service */;
				return Math.Max(tfChunkMaxReaderCount, chunkInitialReaderCount);
			}


			internal static DatabaseOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				ChunkCachePooling = configurationRoot.GetValue<bool>(nameof(ChunkCachePooling)),
				MinFlushDelayMs = configurationRoot.GetValue<double>(nameof(MinFlushDelayMs)),
				DisableScavengeMerging = configurationRoot.GetValue<bool>(nameof(DisableScavengeMerging)),
				MemDb = configurationRoot.GetValue<bool>(nameof(MemDb)),
				Db = configurationRoot.GetString(nameof(Db)),
				ScavengeHistoryMaxAge = configurationRoot.GetValue<int>(nameof(ScavengeHistoryMaxAge)),
				CachedChunks = configurationRoot.GetValue<int>(nameof(CachedChunks)),
				ChunksCacheSize = configurationRoot.GetValue<long>(nameof(ChunksCacheSize)),
				MaxMemTableSize = configurationRoot.GetValue<int>(nameof(MaxMemTableSize)),
				HashCollisionReadLimit = configurationRoot.GetValue<int>(nameof(HashCollisionReadLimit)),
				Index = configurationRoot.GetValue<string?>(nameof(Index)),
				UseIndexBloomFilters = configurationRoot.GetValue<bool>(nameof(UseIndexBloomFilters)),
				IndexCacheSize = configurationRoot.GetValue<int>(nameof(IndexCacheSize)),
				SkipDbVerify = configurationRoot.GetValue<bool>(nameof(SkipDbVerify)),
				WriteThrough = configurationRoot.GetValue<bool>(nameof(WriteThrough)),
				Unbuffered = configurationRoot.GetValue<bool>(nameof(Unbuffered)),
				ChunkInitialReaderCount = configurationRoot.GetValue<int>(nameof(ChunkInitialReaderCount)),
				PrepareTimeoutMs = configurationRoot.GetValue<int>(nameof(PrepareTimeoutMs)),
				CommitTimeoutMs = configurationRoot.GetValue<int>(nameof(CommitTimeoutMs)),
				WriteTimeoutMs = configurationRoot.GetValue<int>(nameof(WriteTimeoutMs)),
				UnsafeDisableFlushToDisk = configurationRoot.GetValue<bool>(nameof(UnsafeDisableFlushToDisk)),
				UnsafeIgnoreHardDelete = configurationRoot.GetValue<bool>(nameof(UnsafeIgnoreHardDelete)),
				SkipIndexVerify = configurationRoot.GetValue<bool>(nameof(SkipIndexVerify)),
				IndexCacheDepth = configurationRoot.GetValue<int>(nameof(IndexCacheDepth)),
				OptimizeIndexMerge = configurationRoot.GetValue<bool>(nameof(OptimizeIndexMerge)),
				AlwaysKeepScavenged = configurationRoot.GetValue<bool>(nameof(AlwaysKeepScavenged)),
				ReduceFileCachePressure = configurationRoot.GetValue<bool>(nameof(ReduceFileCachePressure)),
				InitializationThreads = configurationRoot.GetValue<int>(nameof(InitializationThreads)),
				ReaderThreadsCount = configurationRoot.GetValue<int>(nameof(ReaderThreadsCount)),
				MaxAutoMergeIndexLevel = configurationRoot.GetValue<int>(nameof(MaxAutoMergeIndexLevel)),
				WriteStatsToDb = configurationRoot.GetValue<bool>(nameof(WriteStatsToDb)),
				MaxTruncation = configurationRoot.GetValue<long>(nameof(MaxTruncation)),
				DbLogFormat = configurationRoot.GetValue<DbLogFormat>(nameof(DbLogFormat)),
				StreamExistenceFilterSize = configurationRoot.GetValue<long>(nameof(StreamExistenceFilterSize)),
				ScavengeBackendPageSize = configurationRoot.GetValue<int>(nameof(ScavengeBackendPageSize)),
				ScavengeBackendCacheSize = configurationRoot.GetValue<long>(nameof(ScavengeBackendCacheSize)),
				ScavengeHashUsersCacheCapacity =
					configurationRoot.GetValue<int>(nameof(ScavengeHashUsersCacheCapacity)),
			};
		}

		[Description("gRPC Options")]
		public record GrpcOptions {
			[Description("Controls the period (in milliseconds) after which a keepalive ping " +
			             "is sent on the transport.")]
			public int KeepAliveInterval { get; init; } = 10_000;

			[Description("Controls the amount of time (in milliseconds) the sender of the keepalive ping waits " +
			             "for an acknowledgement. If it does not receive an acknowledgment within this time, " +
			             "it will close the connection.")]
			public int KeepAliveTimeout { get; init; } = 10_000;

			internal static GrpcOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				KeepAliveInterval = configurationRoot.GetValue<int>(nameof(KeepAliveInterval)),
				KeepAliveTimeout = configurationRoot.GetValue<int>(nameof(KeepAliveTimeout))
			};
		}

		[Description("Interface Options")]
		public record InterfaceOptions {
#pragma warning disable 0618
			[Description("Internal IP Address."),
			 Deprecated(
				 "The IntIp parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationIp parameter instead.")]
			[Obsolete("IntIp is deprecated, use ReplicationIp instead")]
			public IPAddress IntIp { get; init; } = IPAddress.Loopback;
			
			private readonly IPAddress _replicationIp = IPAddress.Loopback;
			[Description("The IP Address used by internal replication between nodes in the cluster.")]
			public IPAddress ReplicationIp {
				get {
					return _replicationIp.Equals(IPAddress.Loopback) ? IntIp : _replicationIp;
				}
				init { _replicationIp = value; }
			}

			[Description("External IP Address."),
			 Deprecated(
				 "The ExtIp parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeIp parameter instead.")]
			[Obsolete("ExtIp is deprecated, use NodeIp instead")]
			public IPAddress ExtIp { get; init; } = IPAddress.Loopback;
			
			private readonly IPAddress _nodeIp = IPAddress.Loopback;
			[Description("The IP Address for the node.")]
			public IPAddress NodeIp {
				get {
					return _nodeIp.Equals(IPAddress.Loopback) ? ExtIp : _nodeIp;
				}
				init { _nodeIp = value; }
			}

			[Description("The port to run the HTTP server on."),
			 Deprecated(
				 "The HttpPort parameter has been deprecated as of version 23.10.0. It is recommended to use the NodePort parameter instead.")]
			[Obsolete("HttpPort is deprecated, use NodePort instead")]
			public int HttpPort { get; init; } = 2113;

			private readonly int _nodePort = 2113;

			[Description("The Port to run the HTTP server on.")]
			public int NodePort {
				get {
					return _nodePort == 2113 ? HttpPort : _nodePort;
				}
				init {
					_nodePort = value;
				}
			}

			[Description("Whether to enable external TCP communication"),
			 Deprecated(
				 "The Legacy TCP Client Interface has been deprecated as of version 20.6.0. It is recommended to use gRPC instead.")]
			public bool EnableExternalTcp { get; init; } = false;

			[Description("Internal TCP Port."),
			 Deprecated(
				 "The IntTcpPort parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationPort parameter instead.")]
			[Obsolete("IntTcpPort is deprecated, use ReplicationPort instead")]
			public int IntTcpPort { get; init; } = 1112;

			private readonly int _replicationPort = 1112;
			[Description("The TCP port used by internal replication between nodes in the cluster.")]
			public int ReplicationPort {
				get {
					return _replicationPort == 1112 ? IntTcpPort : _replicationPort;
				}
				init {
					_replicationPort = value;
				}
			}

			[Description("External TCP Port."),
			 Deprecated(
				 "This ExtTcpPort parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeTcpPort parameter instead.")]
			[Obsolete("ExtTcpPort is deprecated, use NodeTcpPort instead")]
			public int ExtTcpPort { get; init; } = 1113;
			
			private readonly int _nodeTcpPort = 1113;
			[Description("The TCP Port that external clients can connect to.")]
			public int NodeTcpPort {
				get {
					return _nodeTcpPort == 1113 ? ExtTcpPort : _nodeTcpPort;
				}
				init {
					_nodeTcpPort = value;
				}
			}

			[Description("Advertise External Tcp Address As."),
			 Deprecated(
				 "The ExtHostAdvertiseAs parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeHostAdvertiseAs instead.")]
			[Obsolete("ExtHostAdvertiseAs is deprecated, use NodeHostAdvertiseAs instead")]
			public string? ExtHostAdvertiseAs { get; init; } = null;

			private readonly string? _nodeHostAdvertiseAs = null;
			[Description("Advertise the Node's host name to other nodes and external clients as.")]
			public string? NodeHostAdvertiseAs {
				get {
					return _nodeHostAdvertiseAs ?? ExtHostAdvertiseAs;
				}
				init {
					_nodeHostAdvertiseAs = value;
				}
			}

			[Description("Advertise Internal Tcp Address As."),
			 Deprecated(
				 "The IntHostAdvertiseAs parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationHostAdvertiseAs parameter instead.")]
			[Obsolete("IntHostAdvertiseAs is deprecated, use ReplicationHostAdvertiseAs instead")]
			public string? IntHostAdvertiseAs { get; init; } = null;
			
			private readonly string? _replicationHostAdvertiseAs = null;
			[Description("Advertise the Replication host name to other nodes in the cluster as.")]
			public string? ReplicationHostAdvertiseAs {
				get {
					return _replicationHostAdvertiseAs ?? IntHostAdvertiseAs;
				}
				init {
					_replicationHostAdvertiseAs = value;
				}
			}

			[Description("Advertise Host in Gossip to Client As.")]
			public string? AdvertiseHostToClientAs { get; init; } = null;

			[Description("Advertise HTTP Port in Gossip to Client As."),
			 Deprecated(
				 "The AdvertiseHttpPortToClientAs parameter has been deprecated as of version 23.10.0. It is recommended to use the AdvertiseNodePortToClientAs parameter instead.")]
			[Obsolete("AdvertiseHttpPortToClientAs is deprecated, use AdvertiseNodePortToClientAs instead")]
			public int AdvertiseHttpPortToClientAs { get; init; } = 0;

			private readonly int _advertiseNodePortToClientAs = 0;

			[Description("Advertise Node Port in Gossip to Client As.")]
			public int AdvertiseNodePortToClientAs {
				get {
					return _advertiseNodePortToClientAs == 0
						? AdvertiseHttpPortToClientAs
						: _advertiseNodePortToClientAs;
				}
				init {
					_advertiseNodePortToClientAs = value;
				}
			}

			[Description("Advertise TCP Port in Gossip to Client As.")]
			public int AdvertiseTcpPortToClientAs { get; init; } = 0;

			[Description("Advertise External Tcp Port As."),
			 Deprecated(
				 "The ExtTcpPortAdvertiseAs parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeTcpPortAdvertiseAs parameter instead.")]
			[Obsolete("ExtTcpPortAdvertiseAs is deprecated, use NodeTcpPortAdvertiseAs instead")]
			public int ExtTcpPortAdvertiseAs { get; init; } = 0;

			private readonly int _nodeTcpPortAdvertiseAs = 0;
			[Description("Advertise Node Tcp Port As.")]
			public int NodeTcpPortAdvertiseAs {
				get {
					return _nodeTcpPortAdvertiseAs == 0 ? ExtTcpPortAdvertiseAs : _nodeTcpPortAdvertiseAs;
				}
				init {
					_nodeTcpPortAdvertiseAs = value;
				}
			}

			[Description("Advertise Http Port As."),
			 Deprecated(
				 "The HttpPortAdvertiseAs parameter has been deprecated as of version 23.10.0. It is recommended to use the NodePortAdvertiseAs parameter instead.")]
			[Obsolete("HttpPortAdvertiseAs is deprecated, use NodePortAdvertiseAs instead")]
			public int HttpPortAdvertiseAs { get; init; } = 0;
			
			private readonly int _nodePortAdvertiseAs = 0;
			[Description("Advertise Http Port As.")]
			public int NodePortAdvertiseAs {
				get {
					return _nodePortAdvertiseAs == 0 ? HttpPortAdvertiseAs : _nodePortAdvertiseAs;
				}
				init {
					_nodePortAdvertiseAs = value;
				}
			}

			[Description("Advertise Internal Tcp Port As."),
			 Deprecated(
				 "The IntTcpPortAdvertiseAs parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationTcpPortAdvertiseAs parameter instead.")]
			[Obsolete("IntTcpPortAdvertiseAs is deprecated, use ReplicationTcpPortAdvertiseAs instead")]
			public int IntTcpPortAdvertiseAs { get; init; } = 0;
			
			private readonly int _replicationTcpPortAdvertiseAs = 0;
			[Description("Advertise Replication Tcp Port As.")]
			public int ReplicationTcpPortAdvertiseAs {
				get {
					return _replicationTcpPortAdvertiseAs == 0 ? IntTcpPortAdvertiseAs : _replicationTcpPortAdvertiseAs;
				}
				init {
					_replicationTcpPortAdvertiseAs = value;
				}
			}

			[Description("Heartbeat timeout for internal TCP sockets."),
			 Deprecated(
				 "The IntTcpHeartbeatTimeout parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationHeartbeatTimeout parameter instead.")]
			[Obsolete("IntTcpHeartbeatTimeout is deprecated, use ReplicationHeartbeatTimeout instead")]
			public int IntTcpHeartbeatTimeout { get; init; } = 700;
			
			private readonly int _replicationHeartbeatTimeout = 700;
			[Description("Heartbeat timeout for Replication TCP sockets.")]
			public int ReplicationHeartbeatTimeout {
				get {
					return _replicationHeartbeatTimeout == 700 ? IntTcpHeartbeatTimeout : _replicationHeartbeatTimeout;
				}
				init {
					_replicationHeartbeatTimeout = value;
				}
			}

			[Description("Heartbeat timeout for external TCP sockets."),
			 Deprecated(
				 "The ExtTcpHeartbeatTimeout parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeHeartbeatTimeout parameter instead.")]
			[Obsolete("ExtTcpHeartbeatTimeout is deprecated, use NodeHeartbeatTimeout instead")]
			public int ExtTcpHeartbeatTimeout { get; init; } = 1_000;
			
			private readonly int _nodeHeartbeatTimeout = 1000;
			[Description("Heartbeat timeout for Node/external TCP sockets.")]
			public int NodeHeartbeatTimeout {
				get {
					return _nodeHeartbeatTimeout == 1000 ? ExtTcpHeartbeatTimeout : _nodeHeartbeatTimeout;
				}
				init {
					_nodeHeartbeatTimeout = value;
				}
			}

			[Description("Heartbeat interval for internal TCP sockets."),
			 Deprecated(
				 "The IntTcpHeartbeatInterval parameter has been deprecated as of version 23.10.0. It is recommended to use the ReplicationHeartbeatInterval parameter instead.")]
			[Obsolete("IntTcpHeartbeatInterval is deprecated, use ReplicationHeartbeatInterval instead")]
			public int IntTcpHeartbeatInterval { get; init; } = 700;

			private readonly int _replicationHeartbeatInterval = 700;
			[Description("Heartbeat interval for Replication TCP sockets.")]
			public int ReplicationHeartbeatInterval {
				get {
					return _replicationHeartbeatInterval == 700
						? IntTcpHeartbeatInterval
						: _replicationHeartbeatInterval;
				}
				init {
					_replicationHeartbeatInterval = value;
				}
			}

			[Description("Heartbeat interval for external TCP sockets."),
			 Deprecated(
				 "The ExtTcpHeartbeatInterval parameter has been deprecated as of version 23.10.0. It is recommended to use the NodeHeartbeatInterval parameter instead.")]
			[Obsolete("ExtTcpHeartbeatInterval is deprecated, use NodeHeartbeatInterval instead")]
			public int ExtTcpHeartbeatInterval { get; init; } = 2_000;

			private readonly int _nodeHeartbeatInterval = 2000;
			[Description("Heartbeat interval for Node/external TCP sockets.")]
			public int NodeHeartbeatInterval {
				get {
					return _nodeHeartbeatInterval == 2000 ? ExtTcpHeartbeatInterval : _nodeHeartbeatInterval;
				}
				init {
					_nodeHeartbeatInterval = value;
				}
			}

			[Description("Whether to allow local connections via a UNIX domain socket.")]
			public bool EnableUnixSocket { get; init; } = false;

			[Description("When enabled, tells a single node to run gossip as if it is a cluster."),
			 Deprecated("The '" + nameof(GossipOnSingleNode) + "' option has been deprecated as of version 21.2.")]
			public bool? GossipOnSingleNode { get; init; } = null;

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

			[Description("Whether to disable secure internal TCP communication."),
			 Deprecated("The '" + nameof(DisableInternalTcpTls) +
			            "' option has been deprecated as of version 20.6.1 and currently has no effect. Please use the '" +
			            nameof(Application.Insecure) + "' option instead.")]
			public bool DisableInternalTcpTls { get; init; } = false;

			[Description("Whether to disable secure external TCP communication."),
			 Deprecated("The '" + nameof(DisableExternalTcpTls) + "' option has been deprecated as of version 20.6.1.")]
			public bool DisableExternalTcpTls { get; init; } = false;

			[Description("Enable AtomPub over HTTP Interface."),
			 Deprecated("AtomPub over HTTP Interface has been deprecated as of version 20.6.0. It is recommended to use gRPC instead")]
			public bool EnableAtomPubOverHttp { get; init; } = false;

			internal static InterfaceOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				GossipOnSingleNode = configurationRoot.GetValue<bool?>(nameof(GossipOnSingleNode)),
				IntIp = IPAddress.Parse(configurationRoot.GetValue<string>(nameof(IntIp)) ??
				                        IPAddress.Loopback.ToString()),
				ReplicationIp = IPAddress.Parse(configurationRoot.GetValue<string>(nameof(ReplicationIp)) ??
				                                IPAddress.Loopback.ToString()),
				ExtIp = IPAddress.Parse(configurationRoot.GetValue<string>(nameof(ExtIp)) ??
				                        IPAddress.Loopback.ToString()),
				NodeIp = IPAddress.Parse(configurationRoot.GetValue<string>(nameof(NodeIp)) ??
				                        IPAddress.Loopback.ToString()),
				HttpPort = configurationRoot.GetValue<int>(nameof(HttpPort)),
				NodePort = configurationRoot.GetValue<int>(nameof(NodePort)),
				EnableExternalTcp = configurationRoot.GetValue<bool>(nameof(EnableExternalTcp)),
				ExtTcpPort = configurationRoot.GetValue<int>(nameof(ExtTcpPort)),
				NodeTcpPort = configurationRoot.GetValue<int>(nameof(NodeTcpPort)),
				IntTcpPort = configurationRoot.GetValue<int>(nameof(IntTcpPort)),
				ReplicationPort = configurationRoot.GetValue<int>(nameof(ReplicationPort)),
				ExtHostAdvertiseAs = configurationRoot.GetValue<string?>(nameof(ExtHostAdvertiseAs)),
				NodeHostAdvertiseAs = configurationRoot.GetValue<string?>(nameof(NodeHostAdvertiseAs)),
				AdvertiseHostToClientAs = configurationRoot.GetValue<string?>(nameof(AdvertiseHostToClientAs)),
				AdvertiseHttpPortToClientAs = configurationRoot.GetValue<int>(nameof(AdvertiseHttpPortToClientAs)),
				AdvertiseNodePortToClientAs = configurationRoot.GetValue<int>(nameof(AdvertiseNodePortToClientAs)),
				AdvertiseTcpPortToClientAs = configurationRoot.GetValue<int>(nameof(AdvertiseTcpPortToClientAs)),
				ExtTcpPortAdvertiseAs = configurationRoot.GetValue<int>(nameof(ExtTcpPortAdvertiseAs)),
				NodeTcpPortAdvertiseAs = configurationRoot.GetValue<int>(nameof(NodeTcpPortAdvertiseAs)),
				IntHostAdvertiseAs = configurationRoot.GetValue<string>(nameof(IntHostAdvertiseAs)),
				ReplicationHostAdvertiseAs = configurationRoot.GetValue<string>(nameof(ReplicationHostAdvertiseAs)),
				HttpPortAdvertiseAs = configurationRoot.GetValue<int>(nameof(HttpPortAdvertiseAs)),
				NodePortAdvertiseAs = configurationRoot.GetValue<int>(nameof(NodePortAdvertiseAs)),
				IntTcpPortAdvertiseAs = configurationRoot.GetValue<int>(nameof(IntTcpPortAdvertiseAs)),
				ReplicationTcpPortAdvertiseAs = configurationRoot.GetValue<int>(nameof(ReplicationTcpPortAdvertiseAs)),
				ExtTcpHeartbeatTimeout = configurationRoot.GetValue<int>(nameof(ExtTcpHeartbeatTimeout)),
				NodeHeartbeatTimeout = configurationRoot.GetValue<int>(nameof(NodeHeartbeatTimeout)),
				IntTcpHeartbeatTimeout = configurationRoot.GetValue<int>(nameof(IntTcpHeartbeatTimeout)),
				ReplicationHeartbeatTimeout = configurationRoot.GetValue<int>(nameof(ReplicationHeartbeatTimeout)),
				ExtTcpHeartbeatInterval = configurationRoot.GetValue<int>(nameof(ExtTcpHeartbeatInterval)),
				NodeHeartbeatInterval = configurationRoot.GetValue<int>(nameof(NodeHeartbeatInterval)),
				IntTcpHeartbeatInterval = configurationRoot.GetValue<int>(nameof(IntTcpHeartbeatInterval)),
				ReplicationHeartbeatInterval = configurationRoot.GetValue<int>(nameof(ReplicationHeartbeatInterval)),
				EnableUnixSocket = configurationRoot.GetValue<bool>(nameof(EnableUnixSocket)),
				ConnectionPendingSendBytesThreshold =
					configurationRoot.GetValue<int>(nameof(ConnectionPendingSendBytesThreshold)),
				ConnectionQueueSizeThreshold = configurationRoot.GetValue<int>(nameof(ConnectionQueueSizeThreshold)),
				DisableAdminUi = configurationRoot.GetValue<bool>(nameof(DisableAdminUi)),
				DisableStatsOnHttp = configurationRoot.GetValue<bool>(nameof(DisableStatsOnHttp)),
				DisableGossipOnHttp = configurationRoot.GetValue<bool>(nameof(DisableGossipOnHttp)),
				EnableTrustedAuth = configurationRoot.GetValue<bool>(nameof(EnableTrustedAuth)),
				DisableInternalTcpTls = configurationRoot.GetValue<bool>(nameof(DisableInternalTcpTls)),
				DisableExternalTcpTls = configurationRoot.GetValue<bool>(nameof(DisableExternalTcpTls)),
				EnableAtomPubOverHttp = configurationRoot.GetValue<bool>(nameof(EnableAtomPubOverHttp))
			};
#pragma warning restore 0618
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

			[Description("The number of minutes a query can be idle before it expires.")]
			public int ProjectionsQueryExpiry { get; init; } = 5;

			[Description("Fault the projection if the Event number that was expected in the stream differs " +
			             "from what is received. This may happen if events have been deleted or expired.")]
			public bool FaultOutOfOrderProjections { get; init; } = false;

			[Description("The time in milliseconds allowed for the compilation phase of user projections")]
			public int ProjectionCompilationTimeout { get; set; } = 500;

			[Description("The maximum execution time in milliseconds for executing a handler in a user projection. It can be overridden for a specific projection by setting ProjectionExecutionTimeout config for that projection")]
			public int ProjectionExecutionTimeout { get; set; } = DefaultProjectionExecutionTimeout;

			internal static ProjectionOptions FromConfiguration(IConfigurationRoot configurationRoot) => new() {
				RunProjections = configurationRoot.GetValue<ProjectionType>(nameof(RunProjections)),
				StartStandardProjections = configurationRoot.GetValue<bool>(nameof(StartStandardProjections)),
				ProjectionThreads = configurationRoot.GetValue<int>(nameof(ProjectionThreads)),
				ProjectionsQueryExpiry = configurationRoot.GetValue<int>(nameof(ProjectionsQueryExpiry)),
				FaultOutOfOrderProjections = configurationRoot.GetValue<bool>(nameof(FaultOutOfOrderProjections)),
				ProjectionCompilationTimeout = configurationRoot.GetValue<int>(nameof(ProjectionCompilationTimeout)),
				ProjectionExecutionTimeout = configurationRoot.GetValue<int>(nameof(ProjectionExecutionTimeout))
			};
		}

		public record UnknownOptions {
			public IReadOnlyList<(string, string)> Options { get; init; }

			public UnknownOptions(IReadOnlyList<(string, string)> keys) {
				Options = keys;
			}

			internal static UnknownOptions FromConfiguration(IConfigurationRoot configurationRoot) {
				var allKnownKeys = typeof(ClusterVNodeOptions).GetProperties()
					.SelectMany(property => property
						.PropertyType
						.GetProperties(BindingFlags.Public | BindingFlags.Instance))
					.Select(x => x.Name)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				var unknownKeys = configurationRoot
					.AsEnumerable()
					.Select(kvp => kvp.Key)
					.Where(key => !allKnownKeys.Contains(key))
					.ToList();

				var unknownOptions = unknownKeys
					.Select(unknownKey => {
						var first = allKnownKeys
							.Select(allowedKey => {
								var distance = Levenshtein.GetDistance(unknownKey, allowedKey);
								return (allowedKey, distance);
							})
							.OrderBy(x => x.distance)
							.First();
						return (unknownKey, first.distance > 5 ? "" : first.allowedKey);
					})
					.ToList();

				return new UnknownOptions(unknownOptions);
			}
		}
	}
}
