using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using EventStore.Common.Configuration;
using EventStore.Common.Utils;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.DataStructures;
using EventStore.Core.Index;
using EventStore.Core.Index.Hashes;
using EventStore.Core.LogAbstraction;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services;
using EventStore.Core.Services.Gossip;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Services.Replication;
using EventStore.Core.Services.RequestManager;
using EventStore.Core.Services.Storage;
using EventStore.Core.Services.Storage.EpochManager;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.Transport.Http;
using EventStore.Core.Services.Transport.Http.Authentication;
using EventStore.Core.Services.Transport.Http.Controllers;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Core.Services.VNode;
using EventStore.Core.Settings;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging;
using EventStore.Core.TransactionLog.Scavenging.Sqlite;
using EventStore.Core.Authentication;
using EventStore.Core.Helpers;
using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.Histograms;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using System.Threading.Tasks;
using EventStore.Common.Exceptions;
using EventStore.Common.Log;
using EventStore.Common.Options;
using EventStore.Core.Authentication.DelegatedAuthentication;
using EventStore.Core.Authentication.PassthroughAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Caching;
using EventStore.Core.Certificates;
using EventStore.Core.Cluster;
using EventStore.Core.Services.PeriodicLogs;
using EventStore.Core.Synchronization;
using EventStore.Core.Telemetry;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.Util;
using EventStore.Native.UnixSignalManager;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Subsystems;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Mono.Unix.Native;
using ILogger = Serilog.ILogger;

namespace EventStore.Core {
	public abstract class ClusterVNode {
		protected static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNode>();

		public static ClusterVNode<TStreamId> Create<TStreamId>(
			ClusterVNodeOptions options,
			ILogFormatAbstractorFactory<TStreamId> logFormatAbstractorFactory,
			AuthenticationProviderFactory authenticationProviderFactory = null,
			AuthorizationProviderFactory authorizationProviderFactory = null,
			IReadOnlyList<IPersistentSubscriptionConsumerStrategyFactory> factories = null,
			CertificateProvider certificateProvider = null,
			MetricsConfiguration metricsConfiguration = null,
			Guid? instanceId = null,
			int debugIndex = 0) {

			return new ClusterVNode<TStreamId>(
				options,
				logFormatAbstractorFactory,
				authenticationProviderFactory,
				authorizationProviderFactory,
				factories,
				certificateProvider,
				metricsConfiguration,
				instanceId: instanceId,
				debugIndex: debugIndex);
		}

		abstract public TFChunkDb Db { get; }
		abstract public GossipAdvertiseInfo GossipAdvertiseInfo { get; }
		abstract public IQueuedHandler MainQueue { get; }
		abstract public ISubscriber MainBus { get; }
		abstract public IReadIndex ReadIndex { get; }
		abstract public QueueStatsManager QueueStatsManager { get; }
		abstract public IStartup Startup { get; }
		abstract public IAuthenticationProvider AuthenticationProvider { get; }
		abstract public AuthorizationGateway AuthorizationGateway { get; }
		abstract public IHttpService HttpService { get; }
		abstract public VNodeInfo NodeInfo { get; }
		abstract public CertificateDelegates.ClientCertificateValidator InternalClientCertificateValidator { get; }
		abstract public Func<X509Certificate2> CertificateSelector { get; }
		abstract public Func<X509Certificate2Collection> IntermediateCertificatesSelector { get; }
		abstract public bool DisableHttps { get; }
		abstract public bool EnableUnixSocket { get; }
		abstract public void Start();
		abstract public Task<ClusterVNode> StartAsync(bool waitUntilRead);
		abstract public Task StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
	}

	public class ClusterVNode<TStreamId> :
		ClusterVNode,
		IHandle<SystemMessage.StateChangeMessage>,
		IHandle<SystemMessage.BecomeShuttingDown>,
		IHandle<SystemMessage.BecomeShutdown>,
		IHandle<SystemMessage.SystemStart>,
		IHandle<ClientMessage.ReloadConfig>{
		private readonly ClusterVNodeOptions _options;
		public override TFChunkDb Db { get; }

		public override GossipAdvertiseInfo GossipAdvertiseInfo { get; }

		public override IQueuedHandler MainQueue {
			get { return _mainQueue; }
		}

		public override ISubscriber MainBus {
			get { return _mainBus; }
		}

		public override IHttpService HttpService {
			get { return _httpService; }
		}

		public override IReadIndex ReadIndex => _readIndex;

		public TimerService TimerService {
			get { return _timerService; }
		}

		public IPublisher NetworkSendService {
			get { return _workersHandler; }
		}

		public override QueueStatsManager QueueStatsManager => _queueStatsManager;

		public override IStartup Startup => _startup;
		
		public override IAuthenticationProvider AuthenticationProvider {
			get { return _authenticationProvider; }
		}

		public override AuthorizationGateway AuthorizationGateway { get; }

		internal MultiQueuedHandler WorkersHandler {
			get { return _workersHandler; }
		}

		public override VNodeInfo NodeInfo { get; }

		public IEnumerable<ISubsystem> Subsystems => _subsystems;

		private readonly IQueuedHandler _mainQueue;
		private readonly ISubscriber _mainBus;

		private readonly ClusterVNodeController<TStreamId> _controller;
		private readonly TimerService _timerService;
		private readonly KestrelHttpService _httpService;
		private readonly ITimeProvider _timeProvider;
		private readonly ISubsystem[] _subsystems;
		private readonly TaskCompletionSource<bool> _shutdownSource = new TaskCompletionSource<bool>();
		private readonly IAuthenticationProvider _authenticationProvider;
		private readonly IAuthorizationProvider _authorizationProvider;
		private readonly IReadIndex<TStreamId> _readIndex;
		private readonly SemaphoreSlimLock _switchChunksLock = new();

		private readonly InMemoryBus[] _workerBuses;
		private readonly MultiQueuedHandler _workersHandler;
		public event EventHandler<VNodeStatusChangeArgs> NodeStatusChanged;
		private readonly List<Task> _tasks = new List<Task>();
		private readonly QueueStatsManager _queueStatsManager;
		private readonly bool _disableHttps;
		private readonly bool _enableUnixSocket;
		private readonly Func<X509Certificate2> _certificateSelector;
		private readonly Func<X509Certificate2Collection> _trustedRootCertsSelector;
		private readonly Func<X509Certificate2Collection> _intermediateCertsSelector;
		private readonly CertificateDelegates.ServerCertificateValidator _internalServerCertificateValidator;
		private readonly CertificateDelegates.ClientCertificateValidator _internalClientCertificateValidator;
		private readonly CertificateDelegates.ClientCertificateValidator _externalClientCertificateValidator;
		private readonly CertificateDelegates.ServerCertificateValidator _externalServerCertificateValidator;
		private readonly CertificateProvider _certificateProvider;
		private readonly ClusterVNodeStartup<TStreamId> _startup;
		private readonly EventStoreClusterClientCache _eventStoreClusterClientCache;

		private int _stopCalled;
		private int _reloadingConfig;

		public IEnumerable<Task> Tasks {
			get { return _tasks; }
		}

		public override CertificateDelegates.ClientCertificateValidator InternalClientCertificateValidator => _internalClientCertificateValidator;
		public override Func<X509Certificate2> CertificateSelector => _certificateSelector;
		public override Func<X509Certificate2Collection> IntermediateCertificatesSelector => _intermediateCertsSelector;
		public override bool DisableHttps => _disableHttps;
		public sealed override bool EnableUnixSocket => _enableUnixSocket;

#if DEBUG
		public TaskCompletionSource<bool> _taskAddedTrigger = new TaskCompletionSource<bool>();
		public object _taskAddLock = new object();
#endif

		protected virtual void OnNodeStatusChanged(VNodeStatusChangeArgs e) {
			EventHandler<VNodeStatusChangeArgs> handler = NodeStatusChanged;
			if (handler != null)
				handler(this, e);
		}

		public ClusterVNode(ClusterVNodeOptions options,
			ILogFormatAbstractorFactory<TStreamId> logFormatAbstractorFactory,
			AuthenticationProviderFactory authenticationProviderFactory = null,
			AuthorizationProviderFactory authorizationProviderFactory = null,
			IReadOnlyList<IPersistentSubscriptionConsumerStrategyFactory>
				additionalPersistentSubscriptionConsumerStrategyFactories = null,
			CertificateProvider certificateProvider = null,
			MetricsConfiguration metricsConfiguration = null,
			IExpiryStrategy expiryStrategy = null,
			Guid? instanceId = null, int debugIndex = 0) {

			_certificateProvider = certificateProvider;
			
			ClusterVNodeOptionsValidator.Validate(options);
			
			ReloadLogOptions(options);

			instanceId ??= Guid.NewGuid();
			if (instanceId == Guid.Empty) {
				throw new ArgumentException("InstanceId may not be empty.", nameof(instanceId));
			}
			
			if (!options.Application.Insecure) {
				ReloadCertificates(options);

				if (_certificateProvider?.TrustedRootCerts == null || _certificateProvider?.Certificate == null) {
					throw new InvalidConfigurationException("A certificate is required unless insecure mode (--insecure) is set.");
				}
			}
			
			_options = options;

#if DEBUG
			AddTask(_taskAddedTrigger.Task);
#endif

			var disableInternalTcpTls = options.Application.Insecure;
			var disableExternalTcpTls = options.Application.Insecure || options.Interface.DisableExternalTcpTls;

			var httpEndPoint = new IPEndPoint(options.Interface.NodeIp, options.Interface.NodePort);
			
			var intTcp = disableInternalTcpTls
				? new IPEndPoint(options.Interface.ReplicationIp, 
					options.Interface.ReplicationPort)
				: null;
			var intSecIp = !disableInternalTcpTls
				? new IPEndPoint(options.Interface.ReplicationIp, 
					options.Interface.ReplicationPort)
				: null;

			var extTcp = disableExternalTcpTls
				? new IPEndPoint(options.Interface.NodeIp, 
					options.Interface.NodeTcpPort)
				: null;
			var extSecIp = !disableExternalTcpTls
				? new IPEndPoint(options.Interface.NodeIp, 
					options.Interface.NodeTcpPort)
				: null;

			var intTcpPortAdvertiseAs = disableInternalTcpTls ? options.Interface.ReplicationTcpPortAdvertiseAs : 0;
			var intSecTcpPortAdvertiseAs = !disableInternalTcpTls ? options.Interface.ReplicationTcpPortAdvertiseAs : 0;
			
			var extTcpPortAdvertiseAs = options.Interface.EnableExternalTcp && disableExternalTcpTls
				? options.Interface.NodeTcpPortAdvertiseAs
				: 0;
			var extSecTcpPortAdvertiseAs = options.Interface.EnableExternalTcp && !disableExternalTcpTls
				? options.Interface.NodeTcpPortAdvertiseAs
				: 0;

			Log.Information("Quorum size set to {quorum}.", options.Cluster.QuorumSize);

			NodeInfo = new VNodeInfo(instanceId.Value, debugIndex, intTcp, intSecIp, extTcp, extSecIp,
				httpEndPoint, options.Cluster.ReadOnlyReplica);

			var dbConfig = CreateDbConfig(
				out var statsHelper,
				out var readerThreadsCount,
				out var workerThreadsCount);

			var trackers = new Trackers();
			metricsConfiguration ??= new();
			MetricsBootstrapper.Bootstrap(metricsConfiguration, dbConfig, trackers);



			IChunkCacheManager cacheManager = new UnmanagedChunkCacheManager();

			if (options.Database.ChunkCachePooling) {
				var chunkTotalSize = dbConfig.ChunkSize + ChunkHeader.Size + ChunkFooter.Size;
				var cacheSizeBytes = dbConfig.MaxChunksCacheSize.RoundUpToMultipleOf(chunkTotalSize);
				var cacheSizePhysicalChunks = cacheSizeBytes / chunkTotalSize;
				var initialBuffers = cacheSizePhysicalChunks + 2;

				var allowanceForPosMap = 16 * 1024 * 1024; //qq hand wave. 6% increase in chunk cache memory usage acceptable

				Log.Information($"#### Chunk cache pooling is enabled");
				Log.Information($"#### Pre-allocating {initialBuffers} initialBuffers based on chunk cache size of {cacheSizePhysicalChunks} chunks.");

				cacheManager = new PoolingChunkCacheManager(
					inner: cacheManager,
					minBufferSize: chunkTotalSize + allowanceForPosMap,
					cleanBuffers: true,
					initialBuffers: (int)initialBuffers);
			} else {
				Log.Information($"#### Chunk cache pooling is disabled");
			}



			Db = new TFChunkDb(
				dbConfig,
				cacheManager: cacheManager,
				tracker: trackers.TransactionFileTracker);

			TFChunkDbConfig CreateDbConfig(
				out SystemStatsHelper statsHelper,
				out int readerThreadsCount,
				out int workerThreadsCount) {

				ICheckpoint writerChk;
				ICheckpoint chaserChk;
				ICheckpoint epochChk;
				ICheckpoint proposalChk;
				ICheckpoint truncateChk;
				ICheckpoint streamExistenceFilterChk;
				//todo(clc) : promote these to file backed checkpoints re:project-io
				ICheckpoint replicationChk = new InMemoryCheckpoint(Checkpoint.Replication, initValue: -1);
				ICheckpoint indexChk = new InMemoryCheckpoint(Checkpoint.Index, initValue: -1);
				var dbPath = options.Database.Db;

				if (options.Database.MemDb) {
					writerChk = new InMemoryCheckpoint(Checkpoint.Writer);
					chaserChk = new InMemoryCheckpoint(Checkpoint.Chaser);
					epochChk = new InMemoryCheckpoint(Checkpoint.Epoch, initValue: -1);
					proposalChk = new InMemoryCheckpoint(Checkpoint.Proposal, initValue: -1);
					truncateChk = new InMemoryCheckpoint(Checkpoint.Truncate, initValue: -1);
					streamExistenceFilterChk = new InMemoryCheckpoint(Checkpoint.StreamExistenceFilter, initValue: -1);
				} else {
					try {
						if (!Directory.Exists(dbPath)) // mono crashes without this check
							Directory.CreateDirectory(dbPath);
					} catch (UnauthorizedAccessException) {
						if (dbPath == Locations.DefaultDataDirectory) {
							Log.Information(
								"Access to path {dbPath} denied. The Event Store database will be created in {fallbackDefaultDataDirectory}",
								dbPath, Locations.FallbackDefaultDataDirectory);
							dbPath = Locations.FallbackDefaultDataDirectory;
							Log.Information("Defaulting DB Path to {dbPath}", dbPath);

							if (!Directory.Exists(dbPath)) // mono crashes without this check
								Directory.CreateDirectory(dbPath);
						} else {
							throw;
						}
					}

					var indexPath = options.Database.Index ?? Path.Combine(dbPath, ESConsts.DefaultIndexDirectoryName);
					var streamExistencePath = Path.Combine(indexPath, ESConsts.StreamExistenceFilterDirectoryName);
					if (!Directory.Exists(streamExistencePath)) {
						Directory.CreateDirectory(streamExistencePath);
					}

					var writerCheckFilename = Path.Combine(dbPath, Checkpoint.Writer + ".chk");
					var chaserCheckFilename = Path.Combine(dbPath, Checkpoint.Chaser + ".chk");
					var epochCheckFilename = Path.Combine(dbPath, Checkpoint.Epoch + ".chk");
					var proposalCheckFilename = Path.Combine(dbPath, Checkpoint.Proposal + ".chk");
					var truncateCheckFilename = Path.Combine(dbPath, Checkpoint.Truncate + ".chk");
					var streamExistenceFilterCheckFilename = Path.Combine(streamExistencePath, Checkpoint.StreamExistenceFilter + ".chk");

					if (OS.IsUnix) {
						Log.Debug("Using File Checkpoints");
						writerChk = new FileCheckpoint(writerCheckFilename, Checkpoint.Writer);
						chaserChk = new FileCheckpoint(chaserCheckFilename, Checkpoint.Chaser);
						epochChk = new FileCheckpoint(epochCheckFilename, Checkpoint.Epoch,
							initValue: -1);
						proposalChk = new FileCheckpoint(proposalCheckFilename, Checkpoint.Proposal,
							initValue: -1);
						truncateChk = new FileCheckpoint(truncateCheckFilename, Checkpoint.Truncate,
							initValue: -1);
						streamExistenceFilterChk = new FileCheckpoint(streamExistenceFilterCheckFilename, Checkpoint.StreamExistenceFilter,
							initValue: -1);
					} else {
						Log.Debug("Using Memory Mapped File Checkpoints");
						writerChk = new MemoryMappedFileCheckpoint(writerCheckFilename, Checkpoint.Writer);
						chaserChk = new MemoryMappedFileCheckpoint(chaserCheckFilename, Checkpoint.Chaser);
						epochChk = new MemoryMappedFileCheckpoint(epochCheckFilename, Checkpoint.Epoch,
							initValue: -1);
						proposalChk = new MemoryMappedFileCheckpoint(proposalCheckFilename, Checkpoint.Proposal,
							initValue: -1);
						truncateChk = new MemoryMappedFileCheckpoint(truncateCheckFilename, Checkpoint.Truncate,
							initValue: -1);
						streamExistenceFilterChk = new MemoryMappedFileCheckpoint(streamExistenceFilterCheckFilename, Checkpoint.StreamExistenceFilter,
							initValue: -1);
					}
				}

				var cache = options.Database.CachedChunks >= 0
					? options.Database.CachedChunks * (long)(TFConsts.ChunkSize + ChunkHeader.Size + ChunkFooter.Size)
					: options.Database.ChunksCacheSize;

				// Calculate automatic configuration changes
				var statsCollectionPeriod = options.Application.StatsPeriodSec > 0
					? (long)options.Application.StatsPeriodSec * 1000
					: Timeout.Infinite;
				statsHelper = new SystemStatsHelper(Log, writerChk.AsReadOnly(), dbPath, statsCollectionPeriod);

				var processorCount = Environment.ProcessorCount;
				readerThreadsCount = ThreadCountCalculator.CalculateReaderThreadCount(options.Database.ReaderThreadsCount, processorCount);
				Log.Information(
					"ReaderThreadsCount set to {readerThreadsCount:N0}. " +
					"Calculated based on processor count of {processorCount:N0} and configured value of {configuredCount:N0}",
					readerThreadsCount,
					processorCount, options.Database.ReaderThreadsCount);

				workerThreadsCount = ThreadCountCalculator.CalculateWorkerThreadCount(options.Application.WorkerThreads, readerThreadsCount);
				Log.Information(
					"WorkerThreads set to {workerThreadsCount:N0}. " +
					"Calculated based on a reader thread count of {readerThreadsCount:N0} and a configured value of {configuredCount:N0}",
					workerThreadsCount,
					readerThreadsCount, options.Application.WorkerThreads);

				return new TFChunkDbConfig(dbPath,
					new VersionedPatternFileNamingStrategy(dbPath, "chunk-"),
					options.Database.ChunkSize,
					cache,
					writerChk,
					chaserChk,
					epochChk,
					proposalChk,
					truncateChk,
					replicationChk,
					indexChk,
					streamExistenceFilterChk,
					options.Database.ChunkInitialReaderCount,
					ClusterVNodeOptions.DatabaseOptions.GetTFChunkMaxReaderCount(
						readerThreadsCount: readerThreadsCount,
						chunkInitialReaderCount: options.Database.ChunkInitialReaderCount),
					options.Database.MemDb,
					options.Database.Unbuffered,
					options.Database.WriteThrough,
					options.Database.OptimizeIndexMerge,
					options.Database.ReduceFileCachePressure,
					options.Database.MaxTruncation);
			}

			var writerCheckpoint = Db.Config.WriterCheckpoint.Read();
			var chaserCheckpoint = Db.Config.ChaserCheckpoint.Read();
			var epochCheckpoint = Db.Config.EpochCheckpoint.Read();
			var truncateCheckpoint = Db.Config.TruncateCheckpoint.Read();
			var streamExistenceFilterCheckpoint = Db.Config.StreamExistenceFilterCheckpoint.Read();

			Log.Information("{description,-25} {instanceId}", "INSTANCE ID:", NodeInfo.InstanceId);
			Log.Information("{description,-25} {path}", "DATABASE:", Db.Config.Path);
			Log.Information("{description,-25} {writerCheckpoint} (0x{writerCheckpoint:X})", "WRITER CHECKPOINT:",
				writerCheckpoint, writerCheckpoint);
			Log.Information("{description,-25} {chaserCheckpoint} (0x{chaserCheckpoint:X})", "CHASER CHECKPOINT:",
				chaserCheckpoint, chaserCheckpoint);
			Log.Information("{description,-25} {epochCheckpoint} (0x{epochCheckpoint:X})", "EPOCH CHECKPOINT:",
				epochCheckpoint, epochCheckpoint);
			Log.Information("{description,-25} {truncateCheckpoint} (0x{truncateCheckpoint:X})", "TRUNCATE CHECKPOINT:",
				truncateCheckpoint, truncateCheckpoint);
			Log.Information("{description,-25} {streamExistenceFilterCheckpoint} (0x{streamExistenceFilterCheckpoint:X})", "STREAM EXISTENCE FILTER CHECKPOINT:",
				streamExistenceFilterCheckpoint, streamExistenceFilterCheckpoint);

			var isSingleNode = options.Cluster.ClusterSize == 1;
			_disableHttps = options.Application.Insecure;
			_enableUnixSocket = options.Interface.EnableUnixSocket;
			_mainBus = new InMemoryBus("MainBus");
			_queueStatsManager = new QueueStatsManager();

			_certificateSelector = () => _certificateProvider?.Certificate;
			_trustedRootCertsSelector = () => _certificateProvider?.TrustedRootCerts;
			_intermediateCertsSelector = () =>
				_certificateProvider?.IntermediateCerts == null
					? null
					: new X509Certificate2Collection(_certificateProvider?.IntermediateCerts);

			_internalServerCertificateValidator = (cert, chain, errors, otherNames) => ValidateServerCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector, otherNames);
			_internalClientCertificateValidator = (cert, chain, errors) => ValidateClientCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector);
			_externalClientCertificateValidator = delegate { return (true, null); };
			_externalServerCertificateValidator = (cert, chain, errors, otherNames) => ValidateServerCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector, otherNames);

			var forwardingProxy = new MessageForwardingProxy();
			if (options.Application.EnableHistograms) {
				HistogramService.CreateHistograms();
				//start watching jitter
				HistogramService.StartJitterMonitor();
			}

			// MISC WORKERS
			_workerBuses = Enumerable.Range(0, workerThreadsCount).Select(queueNum =>
				new InMemoryBus($"Worker #{queueNum + 1} Bus",
					watchSlowMsg: true,
					slowMsgThreshold: TimeSpan.FromMilliseconds(200))).ToArray();
			_workersHandler = new MultiQueuedHandler(
				workerThreadsCount,
				queueNum => new QueuedHandlerThreadPool(_workerBuses[queueNum],
					$"Worker #{queueNum + 1}",
					_queueStatsManager,
					trackers.QueueTrackers,
					groupName: "Workers",
					watchSlowMsg: true,
					slowMsgThreshold: TimeSpan.FromMilliseconds(200)));

			_controller =
				new ClusterVNodeController<TStreamId>(
					(IPublisher)_mainBus, NodeInfo, Db,
					trackers.NodeStatusTracker,
					options, this, forwardingProxy,
					startSubsystems: () => {
						foreach (var subsystem in _subsystems) {
							AddTasks(subsystem.Start());
						}
					});
			_mainQueue = QueuedHandler.CreateQueuedHandler(_controller, "MainQueue", _queueStatsManager,
				trackers.QueueTrackers);

			_controller.SetMainQueue(_mainQueue);

			_eventStoreClusterClientCache = new EventStoreClusterClientCache(_mainQueue,
				(endpoint, publisher) =>
					new EventStoreClusterClient(
						options.Application.Insecure ? Uri.UriSchemeHttp : Uri.UriSchemeHttps,
						endpoint, options.Cluster.DiscoverViaDns ? options.Cluster.ClusterDns : null,
						publisher, _internalServerCertificateValidator, _certificateSelector,
						gossipSendTracker: trackers.GossipTrackers.PushToPeer,
						gossipGetTracker: trackers.GossipTrackers.PullFromPeer));

			_mainBus.Subscribe<ClusterClientMessage.CleanCache>(_eventStoreClusterClientCache);
			_mainBus.Subscribe<SystemMessage.SystemInit>(_eventStoreClusterClientCache);

			//SELF
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(this);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(this);
			_mainBus.Subscribe<SystemMessage.BecomeShutdown>(this);
			_mainBus.Subscribe<SystemMessage.SystemStart>(this);
			_mainBus.Subscribe<ClientMessage.ReloadConfig>(this);

			// MONITORING
			var monitoringInnerBus = new InMemoryBus("MonitoringInnerBus", watchSlowMsg: false);
			var monitoringRequestBus = new InMemoryBus("MonitoringRequestBus", watchSlowMsg: false);
			var monitoringQueue = new QueuedHandlerThreadPool(monitoringInnerBus, "MonitoringQueue", _queueStatsManager,
				trackers.QueueTrackers,
				true,
				TimeSpan.FromMilliseconds(800));

			var monitoring = new MonitoringService(monitoringQueue,
				monitoringRequestBus,
				_mainQueue,
				Db.Config.WriterCheckpoint.AsReadOnly(),
				Db.Config.Path,
				TimeSpan.FromSeconds(options.Application.StatsPeriodSec),
				NodeInfo.HttpEndPoint,
				options.Database.StatsStorage,
				NodeInfo.ExternalTcp,
				NodeInfo.ExternalSecureTcp,
				statsHelper);
			_mainBus.Subscribe(monitoringQueue.WidenFrom<SystemMessage.SystemInit, Message>());
			_mainBus.Subscribe(monitoringQueue.WidenFrom<SystemMessage.StateChangeMessage, Message>());
			_mainBus.Subscribe(monitoringQueue.WidenFrom<SystemMessage.BecomeShuttingDown, Message>());
			_mainBus.Subscribe(monitoringQueue.WidenFrom<SystemMessage.BecomeShutdown, Message>());
			_mainBus.Subscribe(monitoringQueue.WidenFrom<ClientMessage.WriteEventsCompleted, Message>());
			monitoringInnerBus.Subscribe<SystemMessage.SystemInit>(monitoring);
			monitoringInnerBus.Subscribe<SystemMessage.StateChangeMessage>(monitoring);
			monitoringInnerBus.Subscribe<SystemMessage.BecomeShuttingDown>(monitoring);
			monitoringInnerBus.Subscribe<SystemMessage.BecomeShutdown>(monitoring);
			monitoringInnerBus.Subscribe<ClientMessage.WriteEventsCompleted>(monitoring);
			monitoringInnerBus.Subscribe<MonitoringMessage.GetFreshStats>(monitoring);
			monitoringInnerBus.Subscribe<MonitoringMessage.GetFreshTcpConnectionStats>(monitoring);

			// TRUNCATE IF NECESSARY
			var truncPos = Db.Config.TruncateCheckpoint.Read();
			if (truncPos != -1) {
				Log.Information(
					"Truncate checkpoint is present. Truncate: {truncatePosition} (0x{truncatePosition:X}), Writer: {writerCheckpoint} (0x{writerCheckpoint:X}), Chaser: {chaserCheckpoint} (0x{chaserCheckpoint:X}), Epoch: {epochCheckpoint} (0x{epochCheckpoint:X})",
					truncPos, truncPos, writerCheckpoint, writerCheckpoint, chaserCheckpoint, chaserCheckpoint,
					epochCheckpoint, epochCheckpoint);
				var truncator = new TFChunkDbTruncator(Db.Config);
				truncator.TruncateDb(truncPos);
			}

			// DYNAMIC CACHE MANAGER
			Db.Open(!options.Database.SkipDbVerify, threads: options.Database.InitializationThreads);
			var indexPath = options.Database.Index ?? Path.Combine(Db.Config.Path, ESConsts.DefaultIndexDirectoryName);

			var pTableMaxReaderCount = ClusterVNodeOptions.DatabaseOptions.GetPTableMaxReaderCount(readerThreadsCount);
			var readerPool = new ObjectPool<ITransactionFileReader>(
				"ReadIndex readers pool",
				ESConsts.PTableInitialReaderCount,
				pTableMaxReaderCount,
				() => new TFChunkReader(
					Db,
					Db.Config.WriterCheckpoint.AsReadOnly(),
					optimizeReadSideCache: Db.Config.OptimizeReadSideCache));

			var logFormat = logFormatAbstractorFactory.Create(new() {
				InMemory = options.Database.MemDb,
				IndexDirectory = indexPath,
				InitialReaderCount = ESConsts.PTableInitialReaderCount,
				MaxReaderCount = pTableMaxReaderCount,
				StreamExistenceFilterSize = options.Database.StreamExistenceFilterSize,
				StreamExistenceFilterCheckpoint = Db.Config.StreamExistenceFilterCheckpoint,
				TFReaderLeaseFactory = () => new TFReaderLease(readerPool)
			});

			ICacheResizer streamInfoCacheResizer;
			ILRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached> streamLastEventNumberCache;
			ILRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached> streamMetadataCache;
			var totalMem = (long)statsHelper.GetTotalMem();

			if (options.Cluster.StreamInfoCacheCapacity > 0)
				CreateStaticStreamInfoCache(
					options.Cluster.StreamInfoCacheCapacity,
					out streamLastEventNumberCache,
					out streamMetadataCache,
					out streamInfoCacheResizer);
			else
				CreateDynamicStreamInfoCache(
					logFormat.StreamIdSizer,
					totalMem,
					out streamLastEventNumberCache,
					out streamMetadataCache,
					out streamInfoCacheResizer);

			var dynamicCacheManager = new DynamicCacheManager(
				bus: _mainQueue,
				getFreeSystemMem: () => (long) statsHelper.GetFreeMem(),
				getFreeHeapMem: () => GC.GetGCMemoryInfo().FragmentedBytes,
				getGcCollectionCount: () => GC.CollectionCount(GC.MaxGeneration),
				totalMem: totalMem,
				keepFreeMemPercent: 25,
				keepFreeMemBytes: 6L * 1024 * 1024 * 1024, // 6 GiB
				monitoringInterval: TimeSpan.FromSeconds(15),
				minResizeInterval: TimeSpan.FromMinutes(10),
				minResizeThreshold: 200L * 1024 * 1024, // 200 MiB
				rootCacheResizer: new CompositeCacheResizer("cache", 100, streamInfoCacheResizer),
				cacheResourcesTracker: trackers.CacheResourcesTracker);

			_mainBus.Subscribe<MonitoringMessage.DynamicCacheManagerTick>(dynamicCacheManager);
			monitoringRequestBus.Subscribe<MonitoringMessage.InternalStatsRequest>(dynamicCacheManager);

			// STORAGE SUBSYSTEM
			var tableIndex = new TableIndex<TStreamId>(indexPath,
				logFormat.LowHasher,
				logFormat.HighHasher,
				logFormat.EmptyStreamId,
				() => new HashListMemTable(options.IndexBitnessVersion,
					maxSize: options.Database.MaxMemTableSize * 2),
				() => new TFReaderLease(readerPool),
				options.IndexBitnessVersion,
				maxSizeForMemory: options.Database.MaxMemTableSize,
				maxTablesPerLevel: 2,
				inMem: Db.Config.InMemDb,
				skipIndexVerify: options.Database.SkipIndexVerify,
				indexCacheDepth: options.Database.IndexCacheDepth,
				useBloomFilter: options.Database.UseIndexBloomFilters,
				lruCacheSize: options.Database.IndexCacheSize,
				initializationThreads: options.Database.InitializationThreads,
				additionalReclaim: false,
				maxAutoMergeIndexLevel: options.Database.MaxAutoMergeIndexLevel,
				pTableMaxReaderCount: pTableMaxReaderCount,
				statusTracker: trackers.IndexStatusTracker);
			logFormat.StreamNamesProvider.SetTableIndex(tableIndex);

			var readIndex = new ReadIndex<TStreamId>(_mainQueue,
				readerPool,
				tableIndex,
				logFormat.StreamNameIndexConfirmer,
				logFormat.StreamIds,
				logFormat.StreamNamesProvider,
				logFormat.EmptyStreamId,
				logFormat.StreamIdValidator,
				logFormat.StreamIdSizer,
				logFormat.StreamExistenceFilter,
				logFormat.StreamExistenceFilterReader,
				logFormat.EventTypeIndexConfirmer,
				streamLastEventNumberCache,
				streamMetadataCache,
				ESConsts.PerformAdditionlCommitChecks,
				ESConsts.MetaStreamMaxCount,
				options.Database.HashCollisionReadLimit,
				options.Application.SkipIndexScanOnReads,
				Db.Config.ReplicationCheckpoint.AsReadOnly(),
				Db.Config.IndexCheckpoint,
				trackers.IndexStatusTracker,
				trackers.IndexTracker,
				trackers.CacheHitsMissesTracker);
			_readIndex = readIndex;
			var writer = new TFChunkWriter(Db);

			var partitionManager = logFormat.CreatePartitionManager(new TFChunkReader(
					Db,
					Db.Config.WriterCheckpoint.AsReadOnly(),
					optimizeReadSideCache: Db.Config.OptimizeReadSideCache),
				writer);

			var epochManager = new EpochManager<TStreamId>(_mainQueue,
				ESConsts.CachedEpochCount,
				Db.Config.EpochCheckpoint,
				writer,
				initialReaderCount: 1,
				maxReaderCount: 5,
				readerFactory: () => new TFChunkReader(
					Db,
					Db.Config.WriterCheckpoint.AsReadOnly(),
					optimizeReadSideCache: Db.Config.OptimizeReadSideCache),
				logFormat.RecordFactory,
				logFormat.StreamNameIndex,
				logFormat.EventTypeIndex,
				partitionManager,
				NodeInfo.InstanceId);
			epochManager.Init();

			var storageWriter = new ClusterStorageWriterService<TStreamId>(_mainQueue, _mainBus,
				TimeSpan.FromMilliseconds(options.Database.MinFlushDelayMs), Db, writer, readIndex.IndexWriter,
				logFormat.RecordFactory,
				logFormat.StreamNameIndex,
				logFormat.EventTypeIndex,
				logFormat.EmptyEventTypeId,
				logFormat.SystemStreams,
				epochManager, _queueStatsManager,
				trackers.QueueTrackers,
				trackers.WriterFlushSizeTracker,
				trackers.WriterFlushDurationTracker,
				() => readIndex.LastIndexedPosition);
			// subscribes internally
			AddTasks(storageWriter.Tasks);

			monitoringRequestBus.Subscribe<MonitoringMessage.InternalStatsRequest>(storageWriter);

			// Node state listener
			var nodeStatusListener = new NodeStateListenerService(_mainQueue);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(nodeStatusListener);

			var inMemReader = new InMemoryStreamReader(new Dictionary<string, IInMemoryStreamReader> {
				[SystemStreams.NodeStateStream] = nodeStatusListener,
			});

			var storageReader = new StorageReaderService<TStreamId>(_mainQueue, _mainBus, readIndex,
				logFormat.SystemStreams,
				readerThreadsCount, Db.Config.WriterCheckpoint.AsReadOnly(), inMemReader, _queueStatsManager,
				trackers.QueueTrackers);

			_mainBus.Subscribe<SystemMessage.SystemInit>(storageReader);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(storageReader);
			_mainBus.Subscribe<SystemMessage.BecomeShutdown>(storageReader);
			monitoringRequestBus.Subscribe<MonitoringMessage.InternalStatsRequest>(storageReader);

			// PRE-LEADER -> LEADER TRANSITION MANAGEMENT
			var inaugurationManager = new InaugurationManager(
				publisher: _mainQueue,
				replicationCheckpoint: Db.Config.ReplicationCheckpoint,
				indexCheckpoint: Db.Config.IndexCheckpoint,
				statusTracker: trackers.InaugurationStatusTracker);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(inaugurationManager);
			_mainBus.Subscribe<SystemMessage.ChaserCaughtUp>(inaugurationManager);
			_mainBus.Subscribe<SystemMessage.EpochWritten>(inaugurationManager);
			_mainBus.Subscribe<SystemMessage.CheckInaugurationConditions>(inaugurationManager);
			_mainBus.Subscribe<ElectionMessage.ElectionsDone>(inaugurationManager);
			_mainBus.Subscribe<ReplicationTrackingMessage.IndexedTo>(inaugurationManager);
			_mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(inaugurationManager);

			// REPLICATION TRACKING
			var replicationTracker = new ReplicationTrackingService(
				_mainQueue,
				options.Cluster.ClusterSize,
				Db.Config.ReplicationCheckpoint,
				Db.Config.WriterCheckpoint.AsReadOnly());
			AddTask(replicationTracker.Task);
			_mainBus.Subscribe<SystemMessage.SystemInit>(replicationTracker);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(replicationTracker);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(replicationTracker);
			_mainBus.Subscribe<ReplicationTrackingMessage.ReplicaWriteAck>(replicationTracker);
			_mainBus.Subscribe<ReplicationTrackingMessage.WriterCheckpointFlushed>(replicationTracker);
			_mainBus.Subscribe<ReplicationTrackingMessage.LeaderReplicatedTo>(replicationTracker);
			_mainBus.Subscribe<SystemMessage.VNodeConnectionLost>(replicationTracker);
			_mainBus.Subscribe<ReplicationMessage.ReplicaSubscribed>(replicationTracker);
			var indexCommitterService = new IndexCommitterService<TStreamId>(readIndex.IndexCommitter, _mainQueue,
				Db.Config.WriterCheckpoint.AsReadOnly(),
				Db.Config.ReplicationCheckpoint.AsReadOnly(),
				tableIndex, _queueStatsManager);

			AddTask(indexCommitterService.Task);

			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(indexCommitterService);
			_mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(indexCommitterService);
			_mainBus.Subscribe<StorageMessage.CommitAck>(indexCommitterService);
			_mainBus.Subscribe<ClientMessage.MergeIndexes>(indexCommitterService);

			var chaser = new TFChunkChaser(
				Db,
				Db.Config.WriterCheckpoint.AsReadOnly(),
				Db.Config.ChaserCheckpoint,
				Db.Config.OptimizeReadSideCache);

			var storageChaser = new StorageChaser<TStreamId>(
				_mainQueue,
				Db.Config.WriterCheckpoint.AsReadOnly(),
				chaser,
				indexCommitterService,
				epochManager,
				_queueStatsManager);
			AddTask(storageChaser.Task);

#if DEBUG
			_queueStatsManager.InitializeCheckpoints(
				Db.Config.WriterCheckpoint.AsReadOnly(),
				Db.Config.ChaserCheckpoint.AsReadOnly());
#endif
			_mainBus.Subscribe<SystemMessage.SystemInit>(storageChaser);
			_mainBus.Subscribe<SystemMessage.SystemStart>(storageChaser);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(storageChaser);
			// REPLICATION TRACKING END

			var httpPipe = new HttpMessagePipe();
			var httpSendService = new HttpSendService(httpPipe, true, _externalServerCertificateValidator);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(httpSendService);
			SubscribeWorkers(bus => bus.Subscribe<HttpMessage.HttpSend>(httpSendService));

			var grpcSendService = new GrpcSendService(_eventStoreClusterClientCache);
			_mainBus.Subscribe(new WideningHandler<GrpcMessage.SendOverGrpc, Message>(_workersHandler));
			SubscribeWorkers(bus => {
				bus.Subscribe<GrpcMessage.SendOverGrpc>(grpcSendService);
			});

			GossipAdvertiseInfo = GetGossipAdvertiseInfo();
			GossipAdvertiseInfo GetGossipAdvertiseInfo() {
				IPAddress intIpAddress = options.Interface.ReplicationIp;

				IPAddress extIpAddress = options.Interface.NodeIp;

				var intHostToAdvertise = options.Interface.ReplicationHostAdvertiseAs ?? intIpAddress.ToString();
				var extHostToAdvertise = options.Interface.NodeHostAdvertiseAs ?? extIpAddress.ToString();

				if (intIpAddress.Equals(IPAddress.Any) || extIpAddress.Equals(IPAddress.Any)) {
					IPAddress nonLoopbackAddress = IPFinder.GetNonLoopbackAddress();
					IPAddress addressToAdvertise = options.Cluster.ClusterSize > 1 ? nonLoopbackAddress : IPAddress.Loopback;

					if (intIpAddress.Equals(IPAddress.Any) && options.Interface.ReplicationHostAdvertiseAs == null) {
						intHostToAdvertise = addressToAdvertise.ToString();
					}

					if (extIpAddress.Equals(IPAddress.Any) && options.Interface.NodeHostAdvertiseAs == null) {
						extHostToAdvertise = addressToAdvertise.ToString();
					}
				}

				var intTcpEndPoint = NodeInfo.InternalTcp == null
					? null
					: new DnsEndPoint(intHostToAdvertise, intTcpPortAdvertiseAs > 0
						? (options.Interface.ReplicationTcpPortAdvertiseAs)
						: NodeInfo.InternalTcp.Port);

				var intSecureTcpEndPoint = NodeInfo.InternalSecureTcp == null
					? null
					: new DnsEndPoint(intHostToAdvertise, intSecTcpPortAdvertiseAs > 0
						? intSecTcpPortAdvertiseAs
						: NodeInfo.InternalSecureTcp.Port);

				var extTcpEndPoint = NodeInfo.ExternalTcp == null
					? null
					: new DnsEndPoint(extHostToAdvertise, extTcpPortAdvertiseAs > 0
						? extTcpPortAdvertiseAs
						: NodeInfo.ExternalTcp.Port);

				var extSecureTcpEndPoint = NodeInfo.ExternalSecureTcp == null
					? null
					: new DnsEndPoint(extHostToAdvertise, extSecTcpPortAdvertiseAs > 0
						? extSecTcpPortAdvertiseAs
						: NodeInfo.ExternalSecureTcp.Port);
				
				var httpEndPoint = new DnsEndPoint(extHostToAdvertise,
					options.Interface.NodePortAdvertiseAs > 0
						? options.Interface.NodePortAdvertiseAs
						: NodeInfo.HttpEndPoint.GetPort());

				return new GossipAdvertiseInfo(intTcpEndPoint, intSecureTcpEndPoint, extTcpEndPoint,
					extSecureTcpEndPoint, httpEndPoint, options.Interface.ReplicationHostAdvertiseAs,
					options.Interface.NodeHostAdvertiseAs, options.Interface.NodePortAdvertiseAs,
					options.Interface.AdvertiseHostToClientAs, options.Interface.AdvertiseNodePortToClientAs,
					options.Interface.AdvertiseTcpPortToClientAs);
			}

			_httpService = new KestrelHttpService(ServiceAccessibility.Public, _mainQueue, new TrieUriRouter(),
				_workersHandler, options.Application.LogHttpRequests,
				string.IsNullOrEmpty(GossipAdvertiseInfo.AdvertiseHostToClientAs) ? GossipAdvertiseInfo.AdvertiseExternalHostAs : GossipAdvertiseInfo.AdvertiseHostToClientAs,
				GossipAdvertiseInfo.AdvertiseHttpPortToClientAs == 0 ? GossipAdvertiseInfo.AdvertiseHttpPortAs : GossipAdvertiseInfo.AdvertiseHttpPortToClientAs,
				options.Auth.DisableFirstLevelHttpAuthorization,
				NodeInfo.HttpEndPoint);

			var components = new AuthenticationProviderFactoryComponents {
				MainBus = _mainBus,
				MainQueue = _mainQueue,
				WorkerBuses = _workerBuses,
				WorkersQueue = _workersHandler,
				HttpSendService = httpSendService,
				HttpService = _httpService,
			};

			// AUTHENTICATION INFRASTRUCTURE - delegate to plugins
			authorizationProviderFactory ??= !options.Application.Insecure
				? throw new InvalidConfigurationException($"An {nameof(AuthorizationProviderFactory)} is required when running securely.")
				: new AuthorizationProviderFactory(_ => new PassthroughAuthorizationProviderFactory());
			authenticationProviderFactory ??= !options.Application.Insecure
				? throw new InvalidConfigurationException($"An {nameof(AuthenticationProviderFactory)} is required when running securely.")
				: new AuthenticationProviderFactory(_ => new PassthroughAuthenticationProviderFactory());
			additionalPersistentSubscriptionConsumerStrategyFactories ??=
				Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>();

			_authenticationProvider = new DelegatedAuthenticationProvider(
				authenticationProviderFactory.GetFactory(components).Build(
					options.Application.LogFailedAuthenticationAttempts, Log));
			Ensure.NotNull(_authenticationProvider, nameof(_authenticationProvider));

			_authorizationProvider = authorizationProviderFactory
				.GetFactory(new AuthorizationProviderFactoryComponents {
					MainQueue = _mainQueue
				}).Build();
			Ensure.NotNull(_authorizationProvider, "authorizationProvider");

			AuthorizationGateway = new AuthorizationGateway(_authorizationProvider);
			{
				// EXTERNAL TCP
				if (NodeInfo.ExternalTcp != null && options.Interface.EnableExternalTcp) {
					var extTcpService = new TcpService(_mainQueue, NodeInfo.ExternalTcp, _workersHandler,
						TcpServiceType.External, TcpSecurityType.Normal,
						new ClientTcpDispatcher(TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs)),
						TimeSpan.FromMilliseconds(options.Interface.NodeHeartbeatInterval),
						TimeSpan.FromMilliseconds(options.Interface.NodeHeartbeatTimeout),
						_authenticationProvider, AuthorizationGateway, null, null, null,
						options.Interface.ConnectionPendingSendBytesThreshold,
						options.Interface.ConnectionQueueSizeThreshold);
					_mainBus.Subscribe<SystemMessage.SystemInit>(extTcpService);
					_mainBus.Subscribe<SystemMessage.SystemStart>(extTcpService);
					_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(extTcpService);
				}
				// EXTERNAL SECURE TCP
				if (NodeInfo.ExternalSecureTcp != null && options.Interface.EnableExternalTcp) {
					var extSecTcpService = new TcpService(_mainQueue, NodeInfo.ExternalSecureTcp, _workersHandler,
						TcpServiceType.External, TcpSecurityType.Secure,
						new ClientTcpDispatcher(TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs)),
						TimeSpan.FromMilliseconds(options.Interface.NodeHeartbeatInterval),
						TimeSpan.FromMilliseconds(options.Interface.NodeHeartbeatTimeout),
						_authenticationProvider, AuthorizationGateway,
						_certificateSelector, _intermediateCertsSelector, _externalClientCertificateValidator,
						options.Interface.ConnectionPendingSendBytesThreshold,
						options.Interface.ConnectionQueueSizeThreshold);
					_mainBus.Subscribe<SystemMessage.SystemInit>(extSecTcpService);
					_mainBus.Subscribe<SystemMessage.SystemStart>(extSecTcpService);
					_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(extSecTcpService);
				}

				if (!isSingleNode) {
					// INTERNAL TCP
					if (NodeInfo.InternalTcp != null) {
						var intTcpService = new TcpService(_mainQueue, NodeInfo.InternalTcp, _workersHandler,
							TcpServiceType.Internal, TcpSecurityType.Normal,
							new InternalTcpDispatcher(TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs)),
							TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatInterval),
							TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatTimeout),
							_authenticationProvider, AuthorizationGateway, null, null, null, ESConsts.UnrestrictedPendingSendBytes,
						ESConsts.MaxConnectionQueueSize);
						_mainBus.Subscribe<SystemMessage.SystemInit>(intTcpService);
						_mainBus.Subscribe<SystemMessage.SystemStart>(intTcpService);
						_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(intTcpService);
					}
					// INTERNAL SECURE TCP
					if (NodeInfo.InternalSecureTcp != null) {
						var intSecTcpService = new TcpService(_mainQueue, NodeInfo.InternalSecureTcp, _workersHandler,
							TcpServiceType.Internal, TcpSecurityType.Secure,
							new InternalTcpDispatcher(TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs)),
							TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatInterval),
							TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatTimeout),
							_authenticationProvider, AuthorizationGateway,
							_certificateSelector, _intermediateCertsSelector, _internalClientCertificateValidator,
							ESConsts.UnrestrictedPendingSendBytes,
							ESConsts.MaxConnectionQueueSize);
						_mainBus.Subscribe<SystemMessage.SystemInit>(intSecTcpService);
						_mainBus.Subscribe<SystemMessage.SystemStart>(intSecTcpService);
						_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(intSecTcpService);
					}
				}
			}

			SubscribeWorkers(bus => {
				var tcpSendService = new TcpSendService();
				// ReSharper disable RedundantTypeArgumentsOfMethod
				bus.Subscribe<TcpMessage.TcpSend>(tcpSendService);
				// ReSharper restore RedundantTypeArgumentsOfMethod
			});


			var httpAuthenticationProviders = new List<IHttpAuthenticationProvider>();

			foreach (var authenticationScheme in _authenticationProvider.GetSupportedAuthenticationSchemes() ?? Enumerable.Empty<string>()) {
				switch (authenticationScheme)
				{
					case "Basic":
						httpAuthenticationProviders.Add(new BasicHttpAuthenticationProvider(_authenticationProvider));
						break;
					case "Bearer":
						httpAuthenticationProviders.Add(new BearerHttpAuthenticationProvider(_authenticationProvider));
						break;
					case "Insecure":
						httpAuthenticationProviders.Add(new PassthroughHttpAuthenticationProvider(_authenticationProvider));
						break;
					default:
						Log.Warning($"Unsupported Authentication Scheme: {authenticationScheme}");
						break;
				}
			}

			if (!httpAuthenticationProviders.Any()) {
				throw new InvalidConfigurationException($"The server does not support any authentication scheme supported by the '{_authenticationProvider.Name}' authentication provider.");
			}

			if (!options.Application.Insecure) {
				//transport-level authentication providers
				httpAuthenticationProviders.Add(
					new ClientCertificateAuthenticationProvider(() => _certificateProvider.GetReservedNodeCommonName()));

				if (options.Interface.EnableTrustedAuth)
					httpAuthenticationProviders.Add(new TrustedHttpAuthenticationProvider());

				if (EnableUnixSocket)
					httpAuthenticationProviders.Add(new UnixSocketAuthenticationProvider());
			}

			//default authentication provider
			httpAuthenticationProviders.Add(new AnonymousHttpAuthenticationProvider());


			var adminController = new AdminController(_mainQueue, _workersHandler);
			var pingController = new PingController();
			var histogramController = new HistogramController();
			var statController = new StatController(monitoringQueue, _workersHandler);
			var metricsController = new MetricsController();
			var atomController = new AtomController(_mainQueue, _workersHandler,
				options.Application.DisableHttpCaching, TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs));
			var gossipController = new GossipController(_mainQueue, _workersHandler,
				trackers.GossipTrackers.ProcessingRequestFromHttpClient);
			var persistentSubscriptionController =
				new PersistentSubscriptionController(httpSendService, _mainQueue, _workersHandler);
			var infoController = new InfoController(options, new Dictionary<string, bool> {
				["projections"] = options.Projections.RunProjections != ProjectionType.None || options.DevMode.Dev,
				["userManagement"] = options.Auth.AuthenticationType == Opts.AuthenticationTypeDefault &&
				                     !options.Application.Insecure,
				["atomPub"] = options.Interface.EnableAtomPubOverHttp || options.DevMode.Dev

			}, _authenticationProvider);

			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(infoController);

			_httpService.SetupController(persistentSubscriptionController);
			if (!options.Interface.DisableAdminUi)
				_httpService.SetupController(adminController);
			_httpService.SetupController(pingController);
			_httpService.SetupController(infoController);
			if (!options.Interface.DisableStatsOnHttp) {
				_httpService.SetupController(statController);
				_httpService.SetupController(metricsController);
			}
			if (options.Interface.EnableAtomPubOverHttp || options.DevMode.Dev)
				_httpService.SetupController(atomController);
			if (!options.Interface.DisableGossipOnHttp)
				_httpService.SetupController(gossipController);
			_httpService.SetupController(histogramController);

			_mainBus.Subscribe<SystemMessage.SystemInit>(_httpService);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(_httpService);

			SubscribeWorkers(KestrelHttpService.CreateAndSubscribePipeline);

			// REQUEST FORWARDING
			var forwardingService = new RequestForwardingService(_mainQueue, forwardingProxy, TimeSpan.FromSeconds(1));
			_mainBus.Subscribe<SystemMessage.SystemStart>(forwardingService);
			_mainBus.Subscribe<SystemMessage.RequestForwardingTimerTick>(forwardingService);
			_mainBus.Subscribe<ClientMessage.NotHandled>(forwardingService);
			_mainBus.Subscribe<ClientMessage.WriteEventsCompleted>(forwardingService);
			_mainBus.Subscribe<ClientMessage.TransactionStartCompleted>(forwardingService);
			_mainBus.Subscribe<ClientMessage.TransactionWriteCompleted>(forwardingService);
			_mainBus.Subscribe<ClientMessage.TransactionCommitCompleted>(forwardingService);
			_mainBus.Subscribe<ClientMessage.DeleteStreamCompleted>(forwardingService);

			// REQUEST MANAGEMENT
			var requestManagement = new RequestManagementService(
				_mainQueue,
				TimeSpan.FromMilliseconds(options.Database.PrepareTimeoutMs),
				TimeSpan.FromMilliseconds(options.Database.CommitTimeoutMs),
				logFormat.SupportsExplicitTransactions);

			_mainBus.Subscribe<SystemMessage.SystemInit>(requestManagement);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(requestManagement);

			_mainBus.Subscribe<ClientMessage.WriteEvents>(requestManagement);
			_mainBus.Subscribe<ClientMessage.TransactionStart>(requestManagement);
			_mainBus.Subscribe<ClientMessage.TransactionWrite>(requestManagement);
			_mainBus.Subscribe<ClientMessage.TransactionCommit>(requestManagement);
			_mainBus.Subscribe<ClientMessage.DeleteStream>(requestManagement);

			_mainBus.Subscribe<StorageMessage.AlreadyCommitted>(requestManagement);

			_mainBus.Subscribe<StorageMessage.PrepareAck>(requestManagement);
			_mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(requestManagement);
			_mainBus.Subscribe<ReplicationTrackingMessage.IndexedTo>(requestManagement);
			_mainBus.Subscribe<StorageMessage.RequestCompleted>(requestManagement);
			_mainBus.Subscribe<StorageMessage.CommitIndexed>(requestManagement);

			_mainBus.Subscribe<StorageMessage.WrongExpectedVersion>(requestManagement);
			_mainBus.Subscribe<StorageMessage.InvalidTransaction>(requestManagement);
			_mainBus.Subscribe<StorageMessage.StreamDeleted>(requestManagement);

			_mainBus.Subscribe<StorageMessage.RequestManagerTimerTick>(requestManagement);

			// SUBSCRIPTIONS
			var subscrBus = new InMemoryBus("SubscriptionsBus", true, TimeSpan.FromMilliseconds(50));
			var subscrQueue = new QueuedHandlerThreadPool(subscrBus, "Subscriptions", _queueStatsManager,
				trackers.QueueTrackers, false);
			_mainBus.Subscribe(subscrQueue.WidenFrom<SystemMessage.SystemStart, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<SystemMessage.BecomeShuttingDown, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<TcpMessage.ConnectionClosed, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<ClientMessage.SubscribeToStream, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<ClientMessage.FilteredSubscribeToStream, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<ClientMessage.UnsubscribeFromStream, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<SubscriptionMessage.PollStream, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<SubscriptionMessage.CheckPollTimeout, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<StorageMessage.EventCommitted, Message>());
			_mainBus.Subscribe(subscrQueue.WidenFrom<StorageMessage.InMemoryEventCommitted, Message>());

			var subscription = new SubscriptionsService<TStreamId>(_mainQueue, subscrQueue, readIndex);
			subscrBus.Subscribe<SystemMessage.SystemStart>(subscription);
			subscrBus.Subscribe<SystemMessage.BecomeShuttingDown>(subscription);
			subscrBus.Subscribe<TcpMessage.ConnectionClosed>(subscription);
			subscrBus.Subscribe<ClientMessage.SubscribeToStream>(subscription);
			subscrBus.Subscribe<ClientMessage.FilteredSubscribeToStream>(subscription);
			subscrBus.Subscribe<ClientMessage.UnsubscribeFromStream>(subscription);
			subscrBus.Subscribe<SubscriptionMessage.PollStream>(subscription);
			subscrBus.Subscribe<SubscriptionMessage.CheckPollTimeout>(subscription);
			subscrBus.Subscribe<StorageMessage.EventCommitted>(subscription);
			subscrBus.Subscribe<StorageMessage.InMemoryEventCommitted>(subscription);

			// PERSISTENT SUBSCRIPTIONS
			// IO DISPATCHER
			var perSubscrBus = new InMemoryBus("PersistentSubscriptionsBus", true, TimeSpan.FromMilliseconds(50));
			var perSubscrQueue = new QueuedHandlerThreadPool(perSubscrBus, "PersistentSubscriptions", _queueStatsManager,
				trackers.QueueTrackers, false);
			var psubDispatcher = new IODispatcher(_mainQueue, new PublishEnvelope(perSubscrQueue));
			perSubscrBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(psubDispatcher.BackwardReader);
			perSubscrBus.Subscribe<ClientMessage.NotHandled>(psubDispatcher.BackwardReader);
			perSubscrBus.Subscribe<ClientMessage.WriteEventsCompleted>(psubDispatcher.Writer);
			perSubscrBus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(psubDispatcher.ForwardReader);
			perSubscrBus.Subscribe<ClientMessage.ReadAllEventsForwardCompleted>(psubDispatcher.AllForwardReader);
			perSubscrBus.Subscribe<ClientMessage.FilteredReadAllEventsForwardCompleted>(psubDispatcher.AllForwardFilteredReader);
			perSubscrBus.Subscribe<ClientMessage.DeleteStreamCompleted>(psubDispatcher.StreamDeleter);
			perSubscrBus.Subscribe<IODispatcherDelayedMessage>(psubDispatcher);
			perSubscrBus.Subscribe<ClientMessage.NotHandled>(psubDispatcher);
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<SystemMessage.StateChangeMessage, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<TcpMessage.ConnectionClosed, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.CreatePersistentSubscriptionToStream, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.UpdatePersistentSubscriptionToStream, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.DeletePersistentSubscriptionToStream, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.CreatePersistentSubscriptionToAll, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.UpdatePersistentSubscriptionToAll, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.DeletePersistentSubscriptionToAll, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.ConnectToPersistentSubscriptionToStream, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.ConnectToPersistentSubscriptionToAll, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.UnsubscribeFromStream, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.PersistentSubscriptionAckEvents, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.PersistentSubscriptionNackEvents, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.ReplayParkedMessages, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.ReplayParkedMessage, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<ClientMessage.ReadNextNPersistentMessages, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<StorageMessage.EventCommitted, Message>());
			_mainBus.Subscribe(perSubscrQueue
				.WidenFrom<TelemetryMessage.Request, Message>());
			_mainBus.Subscribe(perSubscrQueue
				.WidenFrom<MonitoringMessage.GetAllPersistentSubscriptionStats, Message>());
			_mainBus.Subscribe(
				perSubscrQueue.WidenFrom<MonitoringMessage.GetStreamPersistentSubscriptionStats, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<MonitoringMessage.GetPersistentSubscriptionStats, Message>());
			_mainBus.Subscribe(perSubscrQueue
				.WidenFrom<SubscriptionMessage.PersistentSubscriptionTimerTick, Message>());
			_mainBus.Subscribe(perSubscrQueue.WidenFrom<SubscriptionMessage.PersistentSubscriptionsRestart, Message>());

			//TODO CC can have multiple threads working on subscription if partition
			var consumerStrategyRegistry = new PersistentSubscriptionConsumerStrategyRegistry(_mainQueue, _mainBus,
				additionalPersistentSubscriptionConsumerStrategyFactories);
			var persistentSubscription = new PersistentSubscriptionService<TStreamId>(perSubscrQueue, readIndex, psubDispatcher,
				_mainQueue, consumerStrategyRegistry, trackers.ParkedMessagesTracker);
			perSubscrBus.Subscribe<SystemMessage.BecomeShuttingDown>(persistentSubscription);
			perSubscrBus.Subscribe<SystemMessage.BecomeLeader>(persistentSubscription);
			perSubscrBus.Subscribe<SystemMessage.StateChangeMessage>(persistentSubscription);
			perSubscrBus.Subscribe<TcpMessage.ConnectionClosed>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToStream>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToAll>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.UnsubscribeFromStream>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.PersistentSubscriptionAckEvents>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.PersistentSubscriptionNackEvents>(persistentSubscription);
			perSubscrBus.Subscribe<StorageMessage.EventCommitted>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.CreatePersistentSubscriptionToStream>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToStream>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.DeletePersistentSubscriptionToStream>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.CreatePersistentSubscriptionToAll>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToAll>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.DeletePersistentSubscriptionToAll>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.ReplayParkedMessages>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.ReplayParkedMessage>(persistentSubscription);
			perSubscrBus.Subscribe<ClientMessage.ReadNextNPersistentMessages>(persistentSubscription);
			perSubscrBus.Subscribe<MonitoringMessage.GetAllPersistentSubscriptionStats>(persistentSubscription);
			perSubscrBus.Subscribe<MonitoringMessage.GetStreamPersistentSubscriptionStats>(persistentSubscription);
			perSubscrBus.Subscribe<MonitoringMessage.GetPersistentSubscriptionStats>(persistentSubscription);
			perSubscrBus.Subscribe<TelemetryMessage.Request>(persistentSubscription);
			perSubscrBus.Subscribe<SubscriptionMessage.PersistentSubscriptionTimerTick>(persistentSubscription);
			perSubscrBus.Subscribe<SubscriptionMessage.PersistentSubscriptionsRestart>(persistentSubscription);

			// STORAGE SCAVENGER
			ScavengerFactory scavengerFactory;
			var scavengerDispatcher = new IODispatcher(_mainQueue, new PublishEnvelope(MainQueue));
			_mainBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(scavengerDispatcher.BackwardReader);
			_mainBus.Subscribe<ClientMessage.NotHandled>(scavengerDispatcher.BackwardReader);
			_mainBus.Subscribe<ClientMessage.WriteEventsCompleted>(scavengerDispatcher.Writer);
			_mainBus.Subscribe<IODispatcherDelayedMessage>(scavengerDispatcher);
			_mainBus.Subscribe<ClientMessage.NotHandled>(scavengerDispatcher);

			var newScavenge = true;
			if (newScavenge) {
				// reuse the same buffer; it's quite big.
				var calculatorBuffer = new Calculator<TStreamId>.Buffer(32_768);

				scavengerFactory = new ScavengerFactory((message, scavengerLogger, logger) => {
					// currently on the main queue
					var throttle = new Throttle(
						logger: logger,
						minimumRest: TimeSpan.FromMilliseconds(1000),
						restLoggingThreshold: TimeSpan.FromMilliseconds(10_000),
						activePercent: message.ThrottlePercent ?? 100);

					if (logFormat is not LogFormatAbstractor<string> logFormatV2)
						throw new NotSupportedException("Scavenge is not yet supported on Log V3");

					if (options.Database.MemDb)
						throw new NotSupportedException("Scavenge is not supported on in-memory databases");

					var cancellationCheckPeriod = 1024;

					var longHasher = new CompositeHasher<TStreamId>(logFormat.LowHasher, logFormat.HighHasher);

					// the backends (and therefore connections) are scoped to the run of the scavenge
					// so that we don't keep hold of memory used for the page caches between scavenges
					var backendPool = new ObjectPool<IScavengeStateBackend<TStreamId>>(
						objectPoolName: "scavenge backend pool",
						initialCount: 0, // so that factory is not called on the main queue
						maxCount: TFChunkScavenger.MaxThreadCount + 1,
						factory: () => {
							// not on the main queue
							var scavengeDirectory = Path.Combine(indexPath, "scavenging");
							Directory.CreateDirectory(scavengeDirectory);
							var dbPath = Path.Combine(scavengeDirectory, "scavenging.db");
							var connectionStringBuilder = new SqliteConnectionStringBuilder {
								DataSource = dbPath,
								Pooling = false,
							};
							var connection = new SqliteConnection(connectionStringBuilder.ConnectionString);
							connection.Open();
							Log.Information("Opened scavenging database {scavengeDatabase} with version {version}",
								dbPath, connection.ServerVersion);
							var sqlite = new SqliteScavengeBackend<TStreamId>(
								logger: logger,
								pageSizeInBytes: options.Database.ScavengeBackendPageSize,
								cacheSizeInBytes: options.Database.ScavengeBackendCacheSize);
							sqlite.Initialize(connection);
							return sqlite;
						},
						dispose: backend => backend.Dispose());

					var state = new ScavengeState<TStreamId>(
						logger,
						longHasher,
						logFormat.Metastreams,
						backendPool,
						options.Database.ScavengeHashUsersCacheCapacity);

					var accumulator = new Accumulator<TStreamId>(
						logger: logger,
						chunkSize: TFConsts.ChunkSize,
						metastreamLookup: logFormat.Metastreams,
						chunkReader: new ChunkReaderForAccumulator<TStreamId>(
							Db.Manager,
							logFormat.Metastreams,
							logFormat.StreamIdConverter,
							Db.Config.ReplicationCheckpoint,
							TFConsts.ChunkSize),
						index: new IndexReaderForAccumulator<TStreamId>(readIndex),
						cancellationCheckPeriod: cancellationCheckPeriod,
						throttle: throttle);

					var calculator = new Calculator<TStreamId>(
						logger: logger,
						new IndexReaderForCalculator<TStreamId>(
							readIndex,
							() => new TFReaderLease(readerPool),
							state.LookupUniqueHashUser),
						chunkSize: TFConsts.ChunkSize,
						cancellationCheckPeriod: cancellationCheckPeriod,
						buffer: calculatorBuffer,
						throttle: throttle);

					var chunkExecutor = new ChunkExecutor<TStreamId, ILogRecord>(
						logger,
						logFormat.Metastreams,
						new ChunkManagerForExecutor<TStreamId>(logger, Db.Manager, Db.Config),
						chunkSize: Db.Config.ChunkSize,
						unsafeIgnoreHardDeletes: options.Database.UnsafeIgnoreHardDelete,
						cancellationCheckPeriod: cancellationCheckPeriod,
						threads: message.Threads,
						throttle: throttle);

					var chunkMerger = new ChunkMerger(
						logger: logger,
						mergeChunks: !options.Database.DisableScavengeMerging,
						backend: new OldScavengeChunkMergerBackend(logger, db: Db),
						throttle: throttle);

					var indexExecutor = new IndexExecutor<TStreamId>(
						logger,
						new IndexScavenger(tableIndex),
						new ChunkReaderForIndexExecutor<TStreamId>(() => new TFReaderLease(readerPool)),
						unsafeIgnoreHardDeletes: options.Database.UnsafeIgnoreHardDelete,
						restPeriod: 32_768,
						throttle: throttle);

					var cleaner = new Cleaner(
						logger: logger,
						unsafeIgnoreHardDeletes: options.Database.UnsafeIgnoreHardDelete);

					var scavengePointSource = new ScavengePointSource(logger, scavengerDispatcher);

					return new Scavenger<TStreamId>(
						logger: logger,
						checkPreconditions: () => {
							tableIndex.Visit(table => {
								if (table.Version <= PTableVersions.IndexV1)
									throw new NotSupportedException(
										$"PTable {table.Filename} has version {table.Version}. Scavenge requires V2 index files and above. Please rebuild the indexes to upgrade them.");
							});
						},
						state: state,
						accumulator: accumulator,
						calculator: calculator,
						chunkExecutor: chunkExecutor,
						chunkMerger: chunkMerger,
						indexExecutor: indexExecutor,
						cleaner: cleaner,
						scavengePointSource: scavengePointSource,
						scavengerLogger: scavengerLogger,
						statusTracker: trackers.ScavengeStatusTracker,
						// threshold < 0: execute all chunks, even those with no weight
						// threshold = 0: execute all chunks with weight greater than 0
						// threshold > 0: execute all chunks above a certain weight
						thresholdForNewScavenge: message.Threshold ?? 0,
						syncOnly: message.SyncOnly,
						getThrottleStats: () => throttle.PrettyPrint());
				});

			} else {
				scavengerFactory = new ScavengerFactory((message, scavengerLogger, logger) =>
					new OldScavenger<TStreamId>(
						alwaysKeepScaveged: options.Database.AlwaysKeepScavenged,
						mergeChunks: !options.Database.DisableScavengeMerging,
						startFromChunk: message.StartFromChunk,
						tfChunkScavenger: new TFChunkScavenger<TStreamId>(
							logger: logger,
							db: Db,
							scavengerLog: scavengerLogger,
							tableIndex: tableIndex,
							readIndex: readIndex,
							metastreams: logFormat.SystemStreams,
							unsafeIgnoreHardDeletes: options.Database.UnsafeIgnoreHardDelete,
							threads: message.Threads)));
			}

			var scavengerLogManager = new TFChunkScavengerLogManager(
				nodeEndpoint: NodeInfo.HttpEndPoint.ToString(),
				scavengeHistoryMaxAge: TimeSpan.FromDays(options.Database.ScavengeHistoryMaxAge),
				ioDispatcher: scavengerDispatcher);

			var storageScavenger = new StorageScavenger(
				logManager: scavengerLogManager,
				scavengerFactory: scavengerFactory,
				switchChunksLock: _switchChunksLock);

			// ReSharper disable RedundantTypeArgumentsOfMethod
			_mainBus.Subscribe<ClientMessage.ScavengeDatabase>(storageScavenger);
			_mainBus.Subscribe<ClientMessage.StopDatabaseScavenge>(storageScavenger);
			_mainBus.Subscribe<ClientMessage.GetDatabaseScavenge>(storageScavenger);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(storageScavenger);
			// ReSharper restore RedundantTypeArgumentsOfMethod

			// REDACTION
			var redactionBus = new InMemoryBus("RedactionBus", true, TimeSpan.FromSeconds(2));
			var redactionQueue = new QueuedHandlerThreadPool(redactionBus, "Redaction", _queueStatsManager,
				trackers.QueueTrackers, false);

			_mainBus.Subscribe(redactionQueue.WidenFrom<RedactionMessage.GetEventPosition, Message>());
			_mainBus.Subscribe(redactionQueue.WidenFrom<RedactionMessage.AcquireChunksLock, Message>());
			_mainBus.Subscribe(redactionQueue.WidenFrom<RedactionMessage.SwitchChunk, Message>());
			_mainBus.Subscribe(redactionQueue.WidenFrom<RedactionMessage.ReleaseChunksLock, Message>());
			_mainBus.Subscribe(redactionQueue.WidenFrom<SystemMessage.BecomeShuttingDown, Message>());

			var redactionService = new RedactionService<TStreamId>(redactionQueue, Db, _readIndex, _switchChunksLock);
			redactionBus.Subscribe<RedactionMessage.GetEventPosition>(redactionService);
			redactionBus.Subscribe<RedactionMessage.AcquireChunksLock>(redactionService);
			redactionBus.Subscribe<RedactionMessage.SwitchChunk>(redactionService);
			redactionBus.Subscribe<RedactionMessage.ReleaseChunksLock>(redactionService);
			redactionBus.Subscribe<SystemMessage.BecomeShuttingDown>(redactionService);

			// TIMER
			_timeProvider = new RealTimeProvider();
			var threadBasedScheduler = new ThreadBasedScheduler(_queueStatsManager, trackers.QueueTrackers);
			AddTask(threadBasedScheduler.Task);
			_timerService = new TimerService(threadBasedScheduler);
			_mainBus.Subscribe<SystemMessage.BecomeShutdown>(_timerService);
			_mainBus.Subscribe<TimerMessage.Schedule>(_timerService);

			var memberInfo = MemberInfo.Initial(NodeInfo.InstanceId, _timeProvider.UtcNow, VNodeState.Unknown, true,
				GossipAdvertiseInfo.InternalTcp,
				GossipAdvertiseInfo.InternalSecureTcp,
				GossipAdvertiseInfo.ExternalTcp,
				GossipAdvertiseInfo.ExternalSecureTcp,
				GossipAdvertiseInfo.HttpEndPoint,
				GossipAdvertiseInfo.AdvertiseHostToClientAs,
				GossipAdvertiseInfo.AdvertiseHttpPortToClientAs,
				GossipAdvertiseInfo.AdvertiseTcpPortToClientAs,
				options.Cluster.NodePriority, options.Cluster.ReadOnlyReplica, VersionInfo.Version);

			// TELEMETRY
			var telemetryService = new TelemetryService(
				Db.Manager,
				options,
				_mainQueue,
				new TelemetrySink(options.Application.TelemetryOptout),
				Db.Config.WriterCheckpoint.AsReadOnly(),
				memberInfo.InstanceId);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(telemetryService);
			_mainBus.Subscribe<ElectionMessage.ElectionsDone>(telemetryService);

			if (!isSingleNode) {
				// LEADER REPLICATION
				var leaderReplicationService = new LeaderReplicationService(_mainQueue, NodeInfo.InstanceId, Db,
					_workersHandler,
					epochManager, options.Cluster.ClusterSize,
					options.Cluster.UnsafeAllowSurplusNodes,
					_queueStatsManager);
				AddTask(leaderReplicationService.Task);
				_mainBus.Subscribe<SystemMessage.SystemStart>(leaderReplicationService);
				_mainBus.Subscribe<SystemMessage.StateChangeMessage>(leaderReplicationService);
				_mainBus.Subscribe<SystemMessage.EnablePreLeaderReplication>(leaderReplicationService);				
				_mainBus.Subscribe<ReplicationMessage.ReplicaSubscriptionRequest>(leaderReplicationService);
				_mainBus.Subscribe<ReplicationMessage.ReplicaLogPositionAck>(leaderReplicationService);
				_mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(leaderReplicationService);
				monitoringInnerBus.Subscribe<ReplicationMessage.GetReplicationStats>(leaderReplicationService);

				// REPLICA REPLICATION
				var replicaService = new ReplicaService(_mainQueue, Db, epochManager, _workersHandler,
					_authenticationProvider, AuthorizationGateway,
					GossipAdvertiseInfo.InternalTcp ?? GossipAdvertiseInfo.InternalSecureTcp,
					options.Cluster.ReadOnlyReplica,
					!disableInternalTcpTls, _internalServerCertificateValidator,
					_certificateSelector,
					TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatTimeout),
					TimeSpan.FromMilliseconds(options.Interface.NodeHeartbeatInterval),
					TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs));
				_mainBus.Subscribe<SystemMessage.StateChangeMessage>(replicaService);
				_mainBus.Subscribe<ReplicationMessage.ReconnectToLeader>(replicaService);
				_mainBus.Subscribe<ReplicationMessage.SubscribeToLeader>(replicaService);
				_mainBus.Subscribe<ReplicationMessage.AckLogPosition>(replicaService);
				_mainBus.Subscribe<ClientMessage.TcpForwardMessage>(replicaService);
			}

			// ELECTIONS
			if (!NodeInfo.IsReadOnlyReplica) {
				var electionsService = new ElectionsService(
					_mainQueue,
					memberInfo,
					options.Cluster.ClusterSize,
					Db.Config.WriterCheckpoint.AsReadOnly(),
					Db.Config.ChaserCheckpoint.AsReadOnly(),
					Db.Config.ProposalCheckpoint,
					epochManager,
					() => readIndex.LastIndexedPosition,
					options.Cluster.NodePriority,
					_timeProvider,
					TimeSpan.FromMilliseconds(options.Cluster.LeaderElectionTimeoutMs));
				electionsService.SubscribeMessages(_mainBus);
			}

			if (!isSingleNode || (options.Interface.GossipOnSingleNode ?? true)) {
				// GOSSIP

				var gossipSeedSource = (
					options.Cluster.DiscoverViaDns,
					options.Cluster.ClusterSize > 1,
					options.Cluster.GossipSeed is {Length: >0}) switch {
					(true, true, _) => (IGossipSeedSource)new DnsGossipSeedSource(options.Cluster.ClusterDns,
						options.Cluster.ClusterGossipPort),
					(false, true, false) => throw new InvalidConfigurationException(
						"DNS discovery is disabled, but no gossip seed endpoints have been specified. "
						+ "Specify gossip seeds using the `GossipSeed` option."),
					_ => new KnownEndpointGossipSeedSource(options.Cluster.GossipSeed)
				};

				var gossip = new NodeGossipService(
					_mainQueue,
					gossipSeedSource,
					memberInfo,
					Db.Config.WriterCheckpoint.AsReadOnly(),
					Db.Config.ChaserCheckpoint.AsReadOnly(),
					epochManager, () => readIndex.LastIndexedPosition,
					options.Cluster.NodePriority, TimeSpan.FromMilliseconds(options.Cluster.GossipIntervalMs),
					TimeSpan.FromMilliseconds(options.Cluster.GossipAllowedDifferenceMs),
					TimeSpan.FromMilliseconds(options.Cluster.GossipTimeoutMs),
					TimeSpan.FromSeconds(options.Cluster.DeadMemberRemovalPeriodSec),
					_timeProvider);
				_mainBus.Subscribe<SystemMessage.SystemInit>(gossip);
				_mainBus.Subscribe<GossipMessage.RetrieveGossipSeedSources>(gossip);
				_mainBus.Subscribe<GossipMessage.GotGossipSeedSources>(gossip);
				_mainBus.Subscribe<GossipMessage.Gossip>(gossip);
				_mainBus.Subscribe<GossipMessage.GossipReceived>(gossip);
				_mainBus.Subscribe<GossipMessage.ReadGossip>(gossip);
				_mainBus.Subscribe<GossipMessage.ClientGossip>(gossip);
				_mainBus.Subscribe<SystemMessage.StateChangeMessage>(gossip);
				_mainBus.Subscribe<GossipMessage.GossipSendFailed>(gossip);
				_mainBus.Subscribe<GossipMessage.UpdateNodePriority>(gossip);
				_mainBus.Subscribe<SystemMessage.VNodeConnectionEstablished>(gossip);
				_mainBus.Subscribe<SystemMessage.VNodeConnectionLost>(gossip);
				_mainBus.Subscribe<GossipMessage.GetGossipFailed>(gossip);
				_mainBus.Subscribe<GossipMessage.GetGossipReceived>(gossip);
				_mainBus.Subscribe<ElectionMessage.ElectionsDone>(gossip);
			}
			
			var clusterStateChangeListener = new ClusterMultipleVersionsLogger();
			_mainBus.Subscribe<GossipMessage.GossipUpdated>(clusterStateChangeListener);
			
			// kestrel
			AddTasks(_workersHandler.Start());
			AddTask(_mainQueue.Start());
			AddTask(monitoringQueue.Start());
			AddTask(subscrQueue.Start());
			AddTask(perSubscrQueue.Start());
			AddTask(redactionQueue.Start());

			if (Runtime.IsUnixOrMac) {
				UnixSignalManager.GetInstance().Subscribe(Signum.SIGHUP, () => {
					Log.Information("Reloading the node's configuration since the SIGHUP signal has been received.");
					_mainQueue.Publish(new ClientMessage.ReloadConfig());
				});
			}

			// subsystems
			var http = new[] { _httpService };
			var standardComponents = new StandardComponents(Db, _mainQueue, _mainBus, _timerService, _timeProvider,
					httpSendService, http, _workersHandler, _queueStatsManager, trackers.QueueTrackers);
			_subsystems = options.Subsystems
				.Select(factory => factory.Create(standardComponents))
				.ToArray();

			_startup = new ClusterVNodeStartup<TStreamId>(_subsystems, _mainQueue, monitoringQueue, _mainBus, _workersHandler,
				_authenticationProvider, httpAuthenticationProviders, _authorizationProvider, _readIndex,
				options.Application.MaxAppendSize, TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs),
				expiryStrategy ?? new DefaultExpiryStrategy(),
				_httpService,
				metricsConfiguration,
				trackers,
				options.Cluster.DiscoverViaDns ? options.Cluster.ClusterDns : null);
			_mainBus.Subscribe<SystemMessage.SystemReady>(_startup);
			_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(_startup);
			var certificateExpiryMonitor = new CertificateExpiryMonitor(_mainQueue, _certificateSelector, Log);
			_mainBus.Subscribe<SystemMessage.SystemStart>(certificateExpiryMonitor);
			_mainBus.Subscribe<MonitoringMessage.CheckCertificateExpiry>(certificateExpiryMonitor);
			var periodicLogging = new PeriodicallyLoggingService(_mainQueue, VersionInfo.Version, Log);
			_mainBus.Subscribe<SystemMessage.SystemStart>(periodicLogging);
			_mainBus.Subscribe<MonitoringMessage.CheckEsVersion>(periodicLogging);

			dynamicCacheManager.Start();
		}

		private static void CreateStaticStreamInfoCache(
			int streamInfoCacheCapacity,
			out ILRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached> streamLastEventNumberCache,
			out ILRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached> streamMetadataCache,
			out ICacheResizer streamInfoCacheResizer) {

			streamLastEventNumberCache = new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>(
				"LastEventNumber", streamInfoCacheCapacity);

			streamMetadataCache = new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>(
				"Metadata", streamInfoCacheCapacity);

			streamInfoCacheResizer = new CompositeCacheResizer(
				name: "StreamInfo",
				weight: 100,
				new StaticCacheResizer(ResizerUnit.Entries, streamInfoCacheCapacity, streamLastEventNumberCache),
				new StaticCacheResizer(ResizerUnit.Entries, streamInfoCacheCapacity, streamMetadataCache));
		}

		private static void CreateDynamicStreamInfoCache(
			ISizer<TStreamId> sizer,
			long totalMem,
			out ILRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached> streamLastEventNumberCache,
			out ILRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached> streamMetadataCache,
			out ICacheResizer streamInfoCacheResizer) {

			int LastEventNumberCacheItemSize(TStreamId streamId, IndexBackend<TStreamId>.EventNumberCached eventNumberCached) =>
				LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>.ApproximateItemSize(
					keyRefsSize: sizer.GetSizeInBytes(streamId),
					valueRefsSize: 0);

			streamLastEventNumberCache = new LRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached>(
				"LastEventNumber",
				0,
				LastEventNumberCacheItemSize,
				(streamId, eventNumberCached, keyFreed, valueFreed, nodeFreed) => {
					if (nodeFreed)
						return LastEventNumberCacheItemSize(streamId, eventNumberCached);

					return keyFreed ? sizer.GetSizeInBytes(streamId) : 0;
				}, "bytes");


			int MetadataCacheItemSize(TStreamId streamId, IndexBackend<TStreamId>.MetadataCached metadataCached) =>
				LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>.ApproximateItemSize(
					keyRefsSize: sizer.GetSizeInBytes(streamId),
					valueRefsSize: metadataCached.ApproximateSize - Unsafe.SizeOf<IndexBackend<TStreamId>.MetadataCached>());

			streamMetadataCache = new LRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached>(
				"Metadata",
				0,
				MetadataCacheItemSize,
				(streamId, metadataCached, keyFreed, valueFreed, nodeFreed) => {
					if (nodeFreed)
						return MetadataCacheItemSize(streamId, metadataCached);

					return
						(keyFreed ? sizer.GetSizeInBytes(streamId) : 0) +
						(valueFreed ? metadataCached.ApproximateSize - Unsafe.SizeOf<IndexBackend<TStreamId>.MetadataCached>() : 0);
				}, "bytes");


			const long minCapacity = 100_000_000; // 100 MB

			// beyond a certain point the added heap size costs more in GC than the extra cache is worth
			// higher values than this can still be set manually
			var staticMaxCapacity = 16_000_000_000; // 16GB
			var dynamicMaxCapacity = (long)(0.4 * totalMem);
			var maxCapacity = Math.Min(staticMaxCapacity, dynamicMaxCapacity);

			var minCapacityPerCache = minCapacity / 2;
			var maxCapacityPerCache = maxCapacity / 2;

			streamInfoCacheResizer = new CompositeCacheResizer(
				name: "StreamInfo",
				weight: 100,
				new DynamicCacheResizer(ResizerUnit.Bytes, minCapacityPerCache, maxCapacityPerCache, 60, streamLastEventNumberCache),
				new DynamicCacheResizer(ResizerUnit.Bytes, minCapacityPerCache, maxCapacityPerCache, 40, streamMetadataCache));
		}

		private void SubscribeWorkers(Action<InMemoryBus> setup) {
			foreach (var workerBus in _workerBuses) {
				setup(workerBus);
			}
		}

		public override void Start() {
			_mainQueue.Publish(new SystemMessage.SystemInit());
		}

		public override async Task StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
			if (Interlocked.Exchange(ref _stopCalled, 1) == 1) {
				Log.Warning("Stop was already called.");
				return;
			}

			timeout ??= TimeSpan.FromSeconds(5);
			_mainQueue.Publish(new ClientMessage.RequestShutdown(false, true));

			UnixSignalManager.StopProcessing();

			if (_subsystems != null) {
				foreach (var subsystem in _subsystems) {
					subsystem.Stop();
				}
			}

			var cts = new CancellationTokenSource();

			await using var _ = cts.Token.Register(() => _shutdownSource.TrySetCanceled(cancellationToken)).ConfigureAwait(false);

			cts.CancelAfter(timeout.Value);
			await _shutdownSource.Task.ConfigureAwait(false);
			_switchChunksLock?.Dispose();
		}

		public void Handle(SystemMessage.StateChangeMessage message) {
			OnNodeStatusChanged(new VNodeStatusChangeArgs(message.State));
		}

		public void Handle(SystemMessage.BecomeShuttingDown message) {
			UnixSignalManager.StopProcessing();

			if (_subsystems == null)
				return;
			foreach (var subsystem in _subsystems)
				subsystem.Stop();
		}

		public void Handle(SystemMessage.BecomeShutdown message) {
			_shutdownSource.TrySetResult(true);
		}

		public void Handle(SystemMessage.SystemStart message) {
			_authenticationProvider.Initialize().ContinueWith(t => {
				if (t.Exception != null) {
					_mainQueue.Publish(new AuthenticationMessage.AuthenticationProviderInitializationFailed());
				} else {
					_mainQueue.Publish(new AuthenticationMessage.AuthenticationProviderInitialized());
				}
			});
		}

		public void AddTasks(IEnumerable<Task> tasks) {
#if DEBUG
			foreach (var task in tasks) {
				AddTask(task);
			}
#endif
		}

		public void AddTask(Task task) {
#if DEBUG
			lock (_taskAddLock) {
				_tasks.Add(task);

				//keep reference to old trigger task
				var oldTrigger = _taskAddedTrigger;

				//create and add new trigger task to list
				_taskAddedTrigger = new TaskCompletionSource<bool>();
				_tasks.Add(_taskAddedTrigger.Task);

				//remove old trigger task from list
				_tasks.Remove(oldTrigger.Task);

				//trigger old trigger task
				oldTrigger.SetResult(true);
			}
#endif
		}

		public override async Task<ClusterVNode> StartAsync(bool waitUntilReady) {
			var tcs = new TaskCompletionSource<ClusterVNode>(TaskCreationOptions.RunContinuationsAsynchronously);

			if (waitUntilReady) {
				_mainBus.Subscribe(new AdHocHandler<SystemMessage.SystemReady>(
					_ => tcs.TrySetResult(this)));
			} else {
				tcs.TrySetResult(this);
			}

			Start();

			return await tcs.Task.ConfigureAwait(false);
		}

		public static ValueTuple<bool, string> ValidateServerCertificate(X509Certificate certificate,
			X509Chain chain, SslPolicyErrors sslPolicyErrors, Func<X509Certificate2Collection> intermediateCertsSelector,
			Func<X509Certificate2Collection> trustedRootCertsSelector, string[] otherNames) {
			using var _ = certificate.ConvertToCertificate2(out var certificate2);
			return ValidateCertificate(certificate2, chain, sslPolicyErrors, intermediateCertsSelector, trustedRootCertsSelector, "server", otherNames);
		}

		public static ValueTuple<bool, string> ValidateClientCertificate(X509Certificate certificate,
			X509Chain chain, SslPolicyErrors sslPolicyErrors, Func<X509Certificate2Collection> intermediateCertsSelector, Func<X509Certificate2Collection> trustedRootCertsSelector) {
			using var _ = certificate.ConvertToCertificate2(out var certificate2);
			return ValidateCertificate(certificate2, chain, sslPolicyErrors, intermediateCertsSelector, trustedRootCertsSelector, "client", null);
		}

		private static ValueTuple<bool, string> ValidateCertificate(X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors,
			Func<X509Certificate2Collection> intermediateCertsSelector, Func<X509Certificate2Collection> trustedRootCertsSelector,
			string certificateOrigin, string[] otherNames) {
			if (certificate == null)
				return (false, $"No certificate was provided by the {certificateOrigin}");

			var intermediates = intermediateCertsSelector();

			// add any intermediate certificates received from the origin
			if (chain != null) {
				foreach (var chainElement in chain.ChainElements) {
					if (CertificateUtils.IsValidIntermediateCertificate(chainElement.Certificate, out _)) {
						intermediates ??= new X509Certificate2Collection();
						intermediates.Add(new X509Certificate2(chainElement.Certificate));
					}
				}
			}

			var chainStatus = CertificateUtils.BuildChain(certificate, intermediates, trustedRootCertsSelector(), out var chainStatusInformation);
			if (chainStatus == X509ChainStatusFlags.NoError)
				sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateChainErrors; //clear the RemoteCertificateChainErrors flag
			else
				sslPolicyErrors |= SslPolicyErrors.RemoteCertificateChainErrors; //set the RemoteCertificateChainErrors flag

			if (otherNames != null && (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) {
				if (otherNames.Any(certificate.MatchesName)) { // if we have a match,
					sslPolicyErrors &= ~SslPolicyErrors.RemoteCertificateNameMismatch; // clear the RemoteCertificateNameMismatch flag
				}
			}

			if (sslPolicyErrors != SslPolicyErrors.None) {
				foreach (var status in chainStatusInformation) {
					Log.Error(status);
				}
				return (false, $"The certificate ({certificate.Subject}) provided by the {certificateOrigin} failed validation with the following error(s): {sslPolicyErrors.ToString()} ({chainStatus})");
			}

			return (true, null);
		}

		public void Handle(ClientMessage.ReloadConfig message) {
			if (Interlocked.CompareExchange(ref _reloadingConfig, 1, 0) != 0) {
				Log.Information("The node's configuration reload is already in progress");
				return;
			}

			Task.Run(() => {
				try {
					var options = _options.Reload();
					ReloadLogOptions(options);
					ReloadCertificates(options);
					Log.Information("The node's configuration was successfully reloaded");
				} catch (Exception exc) {
					Log.Error(exc, "An error has occurred while reloading the configuration");
				} finally {
					Interlocked.Exchange(ref _reloadingConfig, 0);
				}
			});
		}

		private void ReloadLogOptions(ClusterVNodeOptions options) {
			if (options.Log.LogLevel != LogLevel.Default) {
				var changed = EventStoreLoggerConfiguration.AdjustMinimumLogLevel(options.Log.LogLevel);
				if (changed) {
					Log.Information($"The log level was adjusted to: {options.Log.LogLevel}");

					if (options.Log.LogLevel > LogLevel.Information) {
						Console.WriteLine($"The log level was adjusted to: {options.Log.LogLevel}");
					}
				}
			}
		}

		private void ReloadCertificates(ClusterVNodeOptions options) {
			if (options.Application.Insecure) {
				Log.Information("Skipping reload of certificates since TLS is disabled.");
				return;
			}
			
			if (_certificateProvider?.LoadCertificates(options) == LoadCertificateResult.VerificationFailed){
				throw new InvalidConfigurationException("Aborting certificate loading due to verification errors.");
			}
		}

		public override string ToString() =>
			$"[{NodeInfo.InstanceId:B}, {NodeInfo.InternalTcp}, {NodeInfo.ExternalTcp}, {NodeInfo.HttpEndPoint}]";
	}
}
