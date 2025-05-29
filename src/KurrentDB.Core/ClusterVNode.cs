// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Cluster;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Subsystems;
using EventStore.Plugins.Transforms;
using KurrentDB.Common.Configuration;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Log;
using KurrentDB.Common.Options;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Authentication;
using KurrentDB.Core.Authentication.DelegatedAuthentication;
using KurrentDB.Core.Authentication.PassthroughAuthentication;
using KurrentDB.Core.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Caching;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Cluster;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Data;
using KurrentDB.Core.DataStructures;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Index;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.LogAbstraction;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Resilience;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Archive;
using KurrentDB.Core.Services.Archive.Naming;
using KurrentDB.Core.Services.Archive.Storage;
using KurrentDB.Core.Services.Gossip;
using KurrentDB.Core.Services.Monitoring;
using KurrentDB.Core.Services.PeriodicLogs;
using KurrentDB.Core.Services.PersistentSubscription;
using KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;
using KurrentDB.Core.Services.Replication;
using KurrentDB.Core.Services.RequestManager;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.Services.Storage.EpochManager;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Services.Transport.Http;
using KurrentDB.Core.Services.Transport.Http.Authentication;
using KurrentDB.Core.Services.Transport.Http.Controllers;
using KurrentDB.Core.Services.Transport.Http.NodeHttpClientFactory;
using KurrentDB.Core.Services.Transport.Tcp;
using KurrentDB.Core.Services.VNode;
using KurrentDB.Core.Settings;
using KurrentDB.Core.Synchronization;
using KurrentDB.Core.Telemetry;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.Chunks.TFChunk;
using KurrentDB.Core.TransactionLog.FileNamingStrategy;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Core.TransactionLog.Scavenging;
using KurrentDB.Core.TransactionLog.Scavenging.DbAccess;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;
using KurrentDB.Core.TransactionLog.Scavenging.Sqlite;
using KurrentDB.Core.TransactionLog.Scavenging.Stages;
using KurrentDB.Core.Transforms;
using KurrentDB.Core.Transforms.Identity;
using KurrentDB.Core.Util;
using KurrentDB.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;
using LogLevel = KurrentDB.Common.Options.LogLevel;
using RuntimeInformation = System.Runtime.RuntimeInformation;
using TelemetryMessage = KurrentDB.Core.Telemetry.TelemetryMessage;

namespace KurrentDB.Core;
public abstract class ClusterVNode {
	protected static readonly ILogger Log = Serilog.Log.ForContext<ClusterVNode>();

	public static readonly TimeSpan ShutdownTimeout =
		ClusterVNodeController.ShutdownTimeout + TimeSpan.FromSeconds(1);

	public static ClusterVNode<TStreamId> Create<TStreamId>(
		ClusterVNodeOptions options,
		ILogFormatAbstractorFactory<TStreamId> logFormatAbstractorFactory,
		AuthenticationProviderFactory authenticationProviderFactory = null,
		AuthorizationProviderFactory authorizationProviderFactory = null,
		VirtualStreamReader virtualStreamReader = null,
		IReadOnlyList<IPersistentSubscriptionConsumerStrategyFactory> factories = null,
		CertificateProvider certificateProvider = null,
		IConfiguration configuration = null,
		Guid? instanceId = null,
		int debugIndex = 0) {

		return new ClusterVNode<TStreamId>(
			options,
			logFormatAbstractorFactory,
			authenticationProviderFactory,
			authorizationProviderFactory,
			virtualStreamReader,
			factories,
			certificateProvider,
			configuration,
			instanceId: instanceId,
			debugIndex: debugIndex);
	}

	abstract public TFChunkDb Db { get; }
	abstract public GossipAdvertiseInfo GossipAdvertiseInfo { get; }
	abstract public IPublisher MainQueue { get; }
	abstract public ISubscriber MainBus { get; }
	abstract public QueueStatsManager QueueStatsManager { get; }
	abstract public IInternalStartup Startup { get; }
	abstract public IAuthenticationProvider AuthenticationProvider { get; }
	abstract public IHttpService HttpService { get; }
	abstract public VNodeInfo NodeInfo { get; }
	abstract public CertificateDelegates.ClientCertificateValidator InternalClientCertificateValidator { get; }
	abstract public Func<X509Certificate2> CertificateSelector { get; }
	abstract public Func<X509Certificate2Collection> IntermediateCertificatesSelector { get; }
	abstract public bool DisableHttps { get; }
	abstract public bool EnableUnixSocket { get; }
	abstract public bool IsShutdown { get; }

	abstract public Task<ClusterVNode> StartAsync(bool waitUntilReady, CancellationToken token);
	abstract public Task StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}

public class ClusterVNode<TStreamId> :
	ClusterVNode,
	IAsyncHandle<SystemMessage.BecomeShuttingDown>,
	IHandle<SystemMessage.BecomeShutdown>,
	IHandle<SystemMessage.SystemStart>,
	IHandle<ClientMessage.ReloadConfig> {

	private readonly ClusterVNodeOptions _options;

	public override TFChunkDb Db { get; }

	public override GossipAdvertiseInfo GossipAdvertiseInfo { get; }

	public override IPublisher MainQueue => _mainQueue;

	public override ISubscriber MainBus => _mainBus;

	public override IHttpService HttpService {
		get { return _httpService; }
	}

	public override QueueStatsManager QueueStatsManager => _queueStatsManager;

	public override IInternalStartup Startup => _startup;

	public override IAuthenticationProvider AuthenticationProvider {
		get { return _authenticationProvider; }
	}

	internal MultiQueuedHandler WorkersHandler {
		get { return _workersHandler; }
	}

	public override VNodeInfo NodeInfo { get; }


	private readonly IPublisher _mainQueue;
	private readonly ISubscriber _mainBus;

	private readonly ClusterVNodeController<TStreamId> _controller;
	private readonly TimerService _timerService;
	private readonly KestrelHttpService _httpService;
	private readonly ITimeProvider _timeProvider;
	private readonly IReadOnlyList<ISubsystem> _subsystems;
	private readonly TaskCompletionSource<bool> _shutdownSource = new();
	private readonly IAuthenticationProvider _authenticationProvider;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly IReadIndex<TStreamId> _readIndex;
	private readonly SemaphoreSlimLock _switchChunksLock = new();

	private readonly InMemoryBus[] _workerBuses;
	private readonly MultiQueuedHandler _workersHandler;
	private readonly List<Task> _tasks = new();
	private readonly QueueStatsManager _queueStatsManager;
	private readonly bool _disableHttps;
	private readonly bool _enableUnixSocket;
	private readonly Func<X509Certificate2> _certificateSelector;
	private readonly Func<X509Certificate2Collection> _trustedRootCertsSelector;
	private readonly Func<X509Certificate2Collection> _intermediateCertsSelector;
	private readonly CertificateDelegates.ServerCertificateValidator _internalServerCertificateValidator;
	private readonly CertificateDelegates.ClientCertificateValidator _internalClientCertificateValidator;
	private readonly CertificateDelegates.ServerCertificateValidator _externalServerCertificateValidator;
	private readonly CertificateProvider _certificateProvider;
	private readonly ClusterVNodeStartup<TStreamId> _startup;
	private readonly Func<CancellationToken, ValueTask> _start;
	private readonly INodeHttpClientFactory _nodeHttpClientFactory;
	private readonly EventStoreClusterClientCache _eventStoreClusterClientCache;

	private int _stopCalled;
	private int _reloadingConfig;
	private PosixSignalRegistration _reloadConfigSignalRegistration;

	public IEnumerable<Task> Tasks {
		get { return _tasks; }
	}

	public override CertificateDelegates.ClientCertificateValidator InternalClientCertificateValidator => _internalClientCertificateValidator;
	public override Func<X509Certificate2> CertificateSelector => _certificateSelector;
	public override Func<X509Certificate2Collection> IntermediateCertificatesSelector => _intermediateCertsSelector;
	public override bool DisableHttps => _disableHttps;
	public sealed override bool EnableUnixSocket => _enableUnixSocket;
	public override bool IsShutdown => _shutdownSource.Task.IsCompleted;

#if DEBUG
	public TaskCompletionSource<bool> _taskAddedTrigger = new();
	public object _taskAddLock = new();
#endif

	public ClusterVNode(ClusterVNodeOptions options,
		ILogFormatAbstractorFactory<TStreamId> logFormatAbstractorFactory,
		AuthenticationProviderFactory authenticationProviderFactory = null,
		AuthorizationProviderFactory authorizationProviderFactory = null,
		VirtualStreamReader virtualStreamReader = null,
		IReadOnlyList<IPersistentSubscriptionConsumerStrategyFactory>
			additionalPersistentSubscriptionConsumerStrategyFactories = null,
		CertificateProvider certificateProvider = null,
		IConfiguration configuration = null,
		IExpiryStrategy expiryStrategy = null,
		Guid? instanceId = null, int debugIndex = 0,
		Action<IServiceCollection> configureAdditionalNodeServices = null) {

		configuration ??= new ConfigurationBuilder().Build();

		LogPluginSubsectionWarnings(configuration);

		_certificateProvider = certificateProvider;

		ClusterVNodeOptionsValidator.Validate(options);

		ReloadLogOptions(options);

		T GetOptions<T>(string subsection) where T : new() => configuration
			.GetSection($"{KurrentConfigurationKeys.Prefix}:{subsection}")
			.Get<T>() ?? new();

		var experimentalOptions = GetOptions<ExperimentalOptions>("Experimental");
		OptionsFormatter.LogConfig("Experimental", experimentalOptions);
		ChunkFileHandle.AsynchronousByDefault = experimentalOptions.AsyncIO;

		var isRunningInContainer = ContainerizedEnvironment.IsRunningInContainer();

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

		var archiveOptions = GetOptions<ArchiveOptions>("Archive");
		OptionsFormatter.LogConfig("Archive", archiveOptions);
		archiveOptions.Validate();

		var disableInternalTcpTls = options.Application.Insecure;
		var disableExternalTcpTls = options.Application.Insecure;
		var nodeTcpOptions = GetOptions<NodeTcpOptions>("TcpPlugin");
		var enableExternalTcp = nodeTcpOptions.EnableExternalTcp;

		var httpEndPoint = new IPEndPoint(options.Interface.NodeIp, options.Interface.NodePort);

		var intTcp = disableInternalTcpTls
			? new IPEndPoint(options.Interface.ReplicationIp,
				options.Interface.ReplicationPort)
			: null;
		var intSecIp = !disableInternalTcpTls
			? new IPEndPoint(options.Interface.ReplicationIp,
				options.Interface.ReplicationPort)
			: null;

		var extTcp = disableExternalTcpTls && enableExternalTcp
			? new IPEndPoint(options.Interface.NodeIp,
				nodeTcpOptions.NodeTcpPort)
			: null;
		var extSecIp = !disableExternalTcpTls && enableExternalTcp
			? new IPEndPoint(options.Interface.NodeIp,
				nodeTcpOptions.NodeTcpPort)
			: null;

		var intTcpPortAdvertiseAs = disableInternalTcpTls ? options.Interface.ReplicationTcpPortAdvertiseAs : 0;
		var intSecTcpPortAdvertiseAs = !disableInternalTcpTls ? options.Interface.ReplicationTcpPortAdvertiseAs : 0;

		var extTcpPortAdvertiseAs = enableExternalTcp && disableExternalTcpTls && nodeTcpOptions.NodeTcpPortAdvertiseAs.HasValue
			? nodeTcpOptions.NodeTcpPortAdvertiseAs.Value!
			: 0;
		var extSecTcpPortAdvertiseAs = enableExternalTcp && !disableExternalTcpTls && nodeTcpOptions.NodeTcpPortAdvertiseAs.HasValue
			? nodeTcpOptions.NodeTcpPortAdvertiseAs.Value!
			: 0;

		Log.Information("Quorum size set to {quorum}.", options.Cluster.QuorumSize);

		NodeInfo = new VNodeInfo(instanceId.Value, debugIndex, intTcp, intSecIp, extTcp, extSecIp,
			httpEndPoint, options.Cluster.ReadOnlyReplica);

		var dbConfig = CreateDbConfig(
			out var statsHelper,
			out var readerThreadsCount,
			out var workerThreadsCount);

		var trackers = new Trackers();
		var metricsConfiguration = MetricsConfiguration.Get(configuration);
		MetricsBootstrapper.Bootstrap(metricsConfiguration, dbConfig, trackers);

		var namingStrategy = new VersionedPatternFileNamingStrategy(dbConfig.Path, "chunk-");
		IChunkFileSystem fileSystem = new ChunkLocalFileSystem(namingStrategy);

		// ARCHIVE
		IArchiveStorageReader archiveReader = NoArchiveReader.Instance;
		var locatorCodec = new PrefixingLocatorCodec();
		if (archiveOptions.Enabled) {
			archiveReader = new ResilientArchiveStorage(
				ResiliencePipelines.RetrySlow,
				ArchiveStorageFactory.Create(
					options: archiveOptions,
					namingStrategy: new ArchiveNamingStrategy(namingStrategy)));

			fileSystem = new FileSystemWithArchive(
				chunkSize: dbConfig.ChunkSize,
				locatorCodec: locatorCodec,
				localFileSystem: fileSystem,
				archive: archiveReader);
		}

		Db = new TFChunkDb(
			dbConfig,
			tracker: trackers.TransactionFileTracker,
			fileSystem: fileSystem,
			transformManager: new DbTransformManager(),
			onChunkSwitched: chunkInfo => {
				_mainQueue.Publish(new SystemMessage.ChunkSwitched(chunkInfo));
			});

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
					Directory.CreateDirectory(dbPath);
					// Ensure that we have write access to the dbPath
					using var _ = File.Create(Path.Combine(dbPath, $"write-attempt-{Guid.NewGuid()}.tmp"),
						bufferSize: 1, FileOptions.DeleteOnClose);
				} catch (UnauthorizedAccessException) {
					// Only use the fallback default directory if we are using a default directory
					if (dbPath == Locations.DefaultDataDirectory || dbPath == Locations.LegacyDataDirectory) {
						Log.Information(
							"Access to path {dbPath} denied. The KurrentDB database will be created in {fallbackDefaultDataDirectory}",
							dbPath, Locations.FallbackDefaultDataDirectory);
						dbPath = Locations.FallbackDefaultDataDirectory;
						Log.Information("Defaulting DB Path to {dbPath}", dbPath);
						Directory.CreateDirectory(dbPath);
					} else {
						throw;
					}
				}

				var indexPath = options.Database.Index ?? Path.Combine(dbPath, ESConsts.DefaultIndexDirectoryName);
				Log.Information("Index Path set to {indexPath}", indexPath);

				var streamExistencePath = Path.Combine(indexPath, ESConsts.StreamExistenceFilterDirectoryName);
				Directory.CreateDirectory(streamExistencePath);

				var writerCheckFilename = Path.Combine(dbPath, Checkpoint.Writer + ".chk");
				var chaserCheckFilename = Path.Combine(dbPath, Checkpoint.Chaser + ".chk");
				var epochCheckFilename = Path.Combine(dbPath, Checkpoint.Epoch + ".chk");
				var proposalCheckFilename = Path.Combine(dbPath, Checkpoint.Proposal + ".chk");
				var truncateCheckFilename = Path.Combine(dbPath, Checkpoint.Truncate + ".chk");
				var streamExistenceFilterCheckFilename = Path.Combine(streamExistencePath, Checkpoint.StreamExistenceFilter + ".chk");

				if (RuntimeInformation.IsUnix) {
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
				? options.Database.CachedChunks * (long)(options.Database.ChunkSize + ChunkHeader.Size + ChunkFooter.Size)
				: options.Database.ChunksCacheSize;

			// Calculate automatic configuration changes
			var statsCollectionPeriod = options.Application.StatsPeriodSec > 0
				? (long)options.Application.StatsPeriodSec * 1000
				: Timeout.Infinite;
			statsHelper = new SystemStatsHelper(Log, writerChk.AsReadOnly(), dbPath, statsCollectionPeriod);

			var processorCount = Environment.ProcessorCount;

			readerThreadsCount =
				ThreadCountCalculator.CalculateReaderThreadCount(options.Database.ReaderThreadsCount,
					processorCount, isRunningInContainer);

			workerThreadsCount =
				ThreadCountCalculator.CalculateWorkerThreadCount(options.Application.WorkerThreads,
					readerThreadsCount, isRunningInContainer);

			return new TFChunkDbConfig(dbPath,
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
				options.Database.MemDb,
				unbuffered: false,
				options.Database.WriteThrough,
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
		_queueStatsManager = new QueueStatsManager();

		_certificateSelector = () => _certificateProvider?.Certificate;
		_trustedRootCertsSelector = () => _certificateProvider?.TrustedRootCerts;
		_intermediateCertsSelector = () =>
			_certificateProvider?.IntermediateCerts == null
				? null
				: new X509Certificate2Collection(_certificateProvider?.IntermediateCerts);

		_internalServerCertificateValidator = (cert, chain, errors, otherNames) => ValidateServerCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector, otherNames);
		_internalClientCertificateValidator = (cert, chain, errors) => ValidateClientCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector);
		_externalServerCertificateValidator = (cert, chain, errors, otherNames) => ValidateServerCertificate(cert, chain, errors, _intermediateCertsSelector, _trustedRootCertsSelector, otherNames);

		var forwardingProxy = new MessageForwardingProxy();

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

		void StartSubsystems() {
			foreach (var subsystem in _subsystems) {
				var subSystemName = subsystem.Name;
				subsystem.Start().ContinueWith(t => {
					if (t.IsCompletedSuccessfully)
						_mainQueue.Publish(new SystemMessage.SubSystemInitialized(subSystemName));
					else
						Log.Error(t.Exception, "Failed to initialize subsystem {subSystemName}", subSystemName);
				});
			}
		}

		_controller =
			new ClusterVNodeController<TStreamId>(
				_queueStatsManager, trackers, NodeInfo, Db,
				trackers.NodeStatusTracker,
				options, this, forwardingProxy,
				startSubsystems: StartSubsystems);

		_mainQueue = _controller.MainQueue;
		_mainBus = _controller.MainBus;

		var shutdownService = new ShutdownService(_mainQueue, NodeInfo);
		_mainBus.Subscribe<SystemMessage.RegisterForGracefulTermination>(shutdownService);
		_mainBus.Subscribe<ClientMessage.RequestShutdown>(shutdownService);
		_mainBus.Subscribe<SystemMessage.ComponentTerminated>(shutdownService);
		_mainBus.Subscribe<SystemMessage.PeripheralShutdownTimeout>(shutdownService);

		var uriScheme = options.Application.Insecure ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
		var clusterDns = options.Cluster.DiscoverViaDns ? options.Cluster.ClusterDns : null;

		_nodeHttpClientFactory = new NodeHttpClientFactory(
			uriScheme,
			_internalServerCertificateValidator,
			_certificateSelector);

		_eventStoreClusterClientCache = new EventStoreClusterClientCache(_mainQueue,
			(endpoint, publisher) =>
				new EventStoreClusterClient(
					publisher, uriScheme,
					endpoint, _nodeHttpClientFactory, clusterDns,
					gossipSendTracker: trackers.GossipTrackers.PushToPeer,
					gossipGetTracker: trackers.GossipTrackers.PullFromPeer));

		_mainBus.Subscribe<ClusterClientMessage.CleanCache>(_eventStoreClusterClientCache);
		_mainBus.Subscribe<SystemMessage.SystemInit>(_eventStoreClusterClientCache);

		//SELF
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
			TimeSpan.FromSeconds(options.Application.StatsPeriodSec),
			NodeInfo.HttpEndPoint,
			options.Database.StatsStorage,
			NodeInfo.ExternalTcp,
			NodeInfo.ExternalSecureTcp,
			statsHelper);

		_mainBus.Subscribe<SystemMessage.SystemInit>(monitoringQueue);
		_mainBus.Subscribe<SystemMessage.StateChangeMessage>(monitoringQueue);
		_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(monitoringQueue);
		_mainBus.Subscribe<SystemMessage.BecomeShutdown>(monitoringQueue);
		_mainBus.Subscribe<ClientMessage.WriteEventsCompleted>(monitoringQueue);
		monitoringInnerBus.Subscribe<SystemMessage.SystemInit>(monitoring);
		monitoringInnerBus.Subscribe<SystemMessage.StateChangeMessage>(monitoring);
		monitoringInnerBus.Subscribe<SystemMessage.BecomeShuttingDown>(monitoring);
		monitoringInnerBus.Subscribe<SystemMessage.BecomeShutdown>(monitoring);
		monitoringInnerBus.Subscribe<ClientMessage.WriteEventsCompleted>(monitoring);
		monitoringInnerBus.Subscribe<MonitoringMessage.GetFreshStats>(monitoring);
		monitoringInnerBus.Subscribe<MonitoringMessage.GetFreshTcpConnectionStats>(monitoring);

		var indexPath = options.Database.Index ?? Path.Combine(Db.Config.Path, ESConsts.DefaultIndexDirectoryName);

		var pTableMaxReaderCount = GetPTableMaxReaderCount(readerThreadsCount);
		var readerPool = new ObjectPool<ITransactionFileReader>(
			"ReadIndex readers pool",
			ESConsts.PTableInitialReaderCount,
			pTableMaxReaderCount,
			() => new TFChunkReader(
				Db,
				Db.Config.WriterCheckpoint.AsReadOnly()));

		var logFormat = logFormatAbstractorFactory.Create(new() {
			InMemory = options.Database.MemDb,
			IndexDirectory = indexPath,
			InitialReaderCount = ESConsts.PTableInitialReaderCount,
			MaxReaderCount = pTableMaxReaderCount,
			StreamExistenceFilterSize = options.Database.StreamExistenceFilterSize,
			StreamExistenceFilterCheckpoint = Db.Config.StreamExistenceFilterCheckpoint,
			TFReaderLeaseFactory = () => new TFReaderLease(readerPool),
			LowHasher = new XXHashUnsafe(),
			HighHasher = new Murmur3AUnsafe(),
		});

		ICacheResizer streamInfoCacheResizer;
		ILRUCache<TStreamId, IndexBackend<TStreamId>.EventNumberCached> streamLastEventNumberCache;
		ILRUCache<TStreamId, IndexBackend<TStreamId>.MetadataCached> streamMetadataCache;
		var totalMem = RuntimeStats.GetTotalMemory();

		if (options.Cluster.StreamInfoCacheCapacity > 0)
			CreateStaticStreamInfoCache(
				options.Cluster.StreamInfoCacheCapacity,
				out streamLastEventNumberCache,
				out streamMetadataCache,
				out streamInfoCacheResizer);
		else if (isRunningInContainer)
			CreateStaticStreamInfoCache(
				ContainerizedEnvironment.StreamInfoCacheCapacity,
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
			getFreeSystemMem: RuntimeStats.GetFreeMemory,
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
				Db.Config.WriterCheckpoint.AsReadOnly()),
			writer);

		var epochManager = new EpochManager<TStreamId>(_mainQueue,
			ESConsts.CachedEpochCount,
			Db.Config.EpochCheckpoint,
			writer,
			initialReaderCount: 1,
			maxReaderCount: 5,
			readerFactory: () => new TFChunkReader(
				Db,
				Db.Config.WriterCheckpoint.AsReadOnly()),
			logFormat.RecordFactory,
			logFormat.StreamNameIndex,
			logFormat.EventTypeIndex,
			partitionManager,
			NodeInfo.InstanceId);

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

		monitoringRequestBus.Subscribe<MonitoringMessage.InternalStatsRequest>(storageWriter);

		// Mem streams
		var memLog = new InMemoryLog();

		// Gossip listener
		var gossipListener = new GossipListenerService(NodeInfo.InstanceId, _mainQueue, memLog);
		_mainBus.Subscribe<GossipMessage.GossipUpdated>(gossipListener);

		// Node state listener
		var nodeStatusListener = new NodeStateListenerService(_mainQueue, memLog);
		_mainBus.Subscribe<SystemMessage.StateChangeMessage>(nodeStatusListener);

		virtualStreamReader ??= new VirtualStreamReader();
		virtualStreamReader.Register(
			gossipListener.Stream,
			nodeStatusListener.Stream
		);

		// Storage Reader
		var storageReader = new StorageReaderService<TStreamId>(_mainQueue, _mainBus, readIndex,
			logFormat.SystemStreams,
			readerThreadsCount, Db.Config.WriterCheckpoint.AsReadOnly(), virtualStreamReader, _queueStatsManager,
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
		_mainBus.Subscribe<StorageMessage.CommitChased>(indexCommitterService);
		_mainBus.Subscribe<ClientMessage.MergeIndexes>(indexCommitterService);

		var chaser = new TFChunkChaser(
			Db,
			Db.Config.WriterCheckpoint.AsReadOnly(),
			Db.Config.ChaserCheckpoint);

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
		_mainBus.Subscribe<GrpcMessage.SendOverGrpc>(_workersHandler);
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
				nodeTcpOptions?.NodeTcpPortAdvertiseAs ?? 0);
		}

		_httpService = new KestrelHttpService(ServiceAccessibility.Public, _mainQueue, new TrieUriRouter(), options.Application.LogHttpRequests,
			string.IsNullOrEmpty(GossipAdvertiseInfo.AdvertiseHostToClientAs) ? GossipAdvertiseInfo.AdvertiseExternalHostAs : GossipAdvertiseInfo.AdvertiseHostToClientAs,
			GossipAdvertiseInfo.AdvertiseHttpPortToClientAs == 0 ? GossipAdvertiseInfo.AdvertiseHttpPortAs : GossipAdvertiseInfo.AdvertiseHttpPortToClientAs,
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
			authenticationProviderFactory
				.GetFactory(components)
				.Build(options.Application.LogFailedAuthenticationAttempts)
		);
		Ensure.NotNull(_authenticationProvider, nameof(_authenticationProvider));

		_authorizationProvider = authorizationProviderFactory
			.GetFactory(new AuthorizationProviderFactoryComponents {
				MainQueue = _mainQueue,
				MainBus = _mainBus
			}).Build();
		Ensure.NotNull(_authorizationProvider, "authorizationProvider");

		var modifiedOptions = options
			.WithPlugableComponent(_authorizationProvider)
			.WithPlugableComponent(_authenticationProvider)
			.WithPlugableComponent(new ArchivePlugableComponent(options.Cluster.Archiver));

		modifiedOptions = modifiedOptions.WithPlugableComponent(new LicensingPlugin(ex => {
			Log.Warning("Shutting down due to licensing error: {Message}", ex.Message);
			MainQueue.Publish(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true));
		}));

		var authorizationGateway = new AuthorizationGateway(_authorizationProvider);
		{
			if (!isSingleNode) {
				// INTERNAL TCP
				if (NodeInfo.InternalTcp != null) {
					var intTcpService = new TcpService(_mainQueue, NodeInfo.InternalTcp, _workersHandler,
						TcpServiceType.Internal, TcpSecurityType.Normal,
						new InternalTcpDispatcher(TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs)),
						TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatInterval),
						TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatTimeout),
						_authenticationProvider, authorizationGateway, null, null, null, ESConsts.UnrestrictedPendingSendBytes,
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
						_authenticationProvider, authorizationGateway,
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
			switch (authenticationScheme) {
				case "Basic":
					httpAuthenticationProviders.Add(new BasicHttpAuthenticationProvider(_authenticationProvider));
					break;
				case "Bearer":
					httpAuthenticationProviders.Add(new BearerHttpAuthenticationProvider(_authenticationProvider));
					break;
				case "Insecure":
					httpAuthenticationProviders.Add(new PassthroughHttpAuthenticationProvider(_authenticationProvider));
					break;
			}
		}

		if (!httpAuthenticationProviders.Any()) {
			throw new InvalidConfigurationException($"The server does not support any authentication scheme supported by the '{_authenticationProvider.Name}' authentication provider.");
		}

		if (!options.Application.Insecure) {
			//transport-level authentication providers
			httpAuthenticationProviders.Add(
				new NodeCertificateAuthenticationProvider(() => _certificateProvider.GetReservedNodeCommonName()));

			if (options.Interface.EnableTrustedAuth)
				httpAuthenticationProviders.Add(new TrustedHttpAuthenticationProvider());

			if (EnableUnixSocket)
				httpAuthenticationProviders.Add(new UnixSocketAuthenticationProvider());
		}

		//default authentication provider
		httpAuthenticationProviders.Add(new AnonymousHttpAuthenticationProvider());

		var adminController = new AdminController(_mainQueue, _workersHandler);
		var pingController = new PingController();
		var statController = new StatController(monitoringQueue, _workersHandler);
		var metricsController = new MetricsController();
		var atomController = new AtomController(_mainQueue, _workersHandler,
			options.Application.DisableHttpCaching, options.Application.MaxAppendEventSize, TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs));
		var gossipController = new GossipController(_mainQueue, _workersHandler,
			trackers.GossipTrackers.ProcessingRequestFromHttpClient);
		var persistentSubscriptionController =
			new PersistentSubscriptionController(httpSendService, _mainQueue, _workersHandler);

		var infoController = new InfoController(
			options,
			new Dictionary<string, bool> {
				["projections"] = options.Projection.RunProjections != ProjectionType.None || options.DevMode.Dev,
				["userManagement"] = options.Auth.AuthenticationType == Opts.AuthenticationTypeDefault && !options.Application.Insecure,
				["atomPub"] = options.Interface.EnableAtomPubOverHttp || options.DevMode.Dev
			},
			_authenticationProvider
		);

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

		_mainBus.Subscribe<StorageMessage.UncommittedPrepareChased>(requestManagement);
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
		_mainBus.Subscribe<SystemMessage.SystemStart>(subscrQueue);
		_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(subscrQueue);
		_mainBus.Subscribe<TcpMessage.ConnectionClosed>(subscrQueue);
		_mainBus.Subscribe<ClientMessage.SubscribeToStream>(subscrQueue);
		_mainBus.Subscribe<ClientMessage.FilteredSubscribeToStream>(subscrQueue);
		_mainBus.Subscribe<ClientMessage.UnsubscribeFromStream>(subscrQueue);
		_mainBus.Subscribe<SubscriptionMessage.DropSubscription>(subscrQueue);
		_mainBus.Subscribe<SubscriptionMessage.PollStream>(subscrQueue);
		_mainBus.Subscribe<SubscriptionMessage.CheckPollTimeout>(subscrQueue);
		_mainBus.Subscribe<StorageMessage.EventCommitted>(subscrQueue);
		_mainBus.Subscribe<StorageMessage.InMemoryEventCommitted>(subscrQueue);

		var subscription = new SubscriptionsService<TStreamId>(_mainQueue, subscrQueue, _authorizationProvider, readIndex, virtualStreamReader);
		subscrBus.Subscribe<SystemMessage.SystemStart>(subscription);
		subscrBus.Subscribe<SystemMessage.BecomeShuttingDown>(subscription);
		subscrBus.Subscribe<TcpMessage.ConnectionClosed>(subscription);
		subscrBus.Subscribe<ClientMessage.SubscribeToStream>(subscription);
		subscrBus.Subscribe<ClientMessage.FilteredSubscribeToStream>(subscription);
		subscrBus.Subscribe<ClientMessage.UnsubscribeFromStream>(subscription);
		subscrBus.Subscribe<SubscriptionMessage.DropSubscription>(subscription);
		subscrBus.Subscribe<SubscriptionMessage.PollStream>(subscription);
		subscrBus.Subscribe<SubscriptionMessage.CheckPollTimeout>(subscription);
		subscrBus.Subscribe<StorageMessage.EventCommitted>(subscription);
		subscrBus.Subscribe<StorageMessage.InMemoryEventCommitted>(subscription);

		// PERSISTENT SUBSCRIPTIONS
		// IO DISPATCHER
		var perSubscrBus = new InMemoryBus("PersistentSubscriptionsBus", true, TimeSpan.FromMilliseconds(50));
		var perSubscrQueue = new QueuedHandlerThreadPool(perSubscrBus, "PersistentSubscriptions", _queueStatsManager,
			trackers.QueueTrackers, false);
		var psubDispatcher = new IODispatcher(_mainQueue, perSubscrQueue);
		perSubscrBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(psubDispatcher.BackwardReader);
		perSubscrBus.Subscribe<ClientMessage.NotHandled>(psubDispatcher.BackwardReader);
		perSubscrBus.Subscribe<ClientMessage.WriteEventsCompleted>(psubDispatcher.Writer);
		perSubscrBus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(psubDispatcher.ForwardReader);
		perSubscrBus.Subscribe<ClientMessage.ReadAllEventsForwardCompleted>(psubDispatcher.AllForwardReader);
		perSubscrBus.Subscribe<ClientMessage.FilteredReadAllEventsForwardCompleted>(psubDispatcher.AllForwardFilteredReader);
		perSubscrBus.Subscribe<ClientMessage.DeleteStreamCompleted>(psubDispatcher.StreamDeleter);
		perSubscrBus.Subscribe<IODispatcherDelayedMessage>(psubDispatcher);
		perSubscrBus.Subscribe<ClientMessage.NotHandled>(psubDispatcher);
		_mainBus.Subscribe<SystemMessage.StateChangeMessage>(perSubscrQueue);
		_mainBus.Subscribe<TcpMessage.ConnectionClosed>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.CreatePersistentSubscriptionToStream>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToStream>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.DeletePersistentSubscriptionToStream>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.CreatePersistentSubscriptionToAll>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.UpdatePersistentSubscriptionToAll>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.DeletePersistentSubscriptionToAll>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToStream>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.ConnectToPersistentSubscriptionToAll>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.UnsubscribeFromStream>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.PersistentSubscriptionAckEvents>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.PersistentSubscriptionNackEvents>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.ReplayParkedMessages>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.ReplayParkedMessage>(perSubscrQueue);
		_mainBus.Subscribe<ClientMessage.ReadNextNPersistentMessages>(perSubscrQueue);
		_mainBus.Subscribe<StorageMessage.EventCommitted>(perSubscrQueue);
		_mainBus.Subscribe<TelemetryMessage.Request>(perSubscrQueue);
		_mainBus.Subscribe<MonitoringMessage.GetAllPersistentSubscriptionStats>(perSubscrQueue);
		_mainBus.Subscribe<MonitoringMessage.GetStreamPersistentSubscriptionStats>(perSubscrQueue);
		_mainBus.Subscribe<MonitoringMessage.GetPersistentSubscriptionStats>(perSubscrQueue);
		_mainBus.Subscribe<SubscriptionMessage.PersistentSubscriptionTimerTick>(perSubscrQueue);
		_mainBus.Subscribe<SubscriptionMessage.PersistentSubscriptionsRestart>(perSubscrQueue);

		//TODO CC can have multiple threads working on subscription if partition
		var consumerStrategyRegistry = new PersistentSubscriptionConsumerStrategyRegistry(_mainQueue, _mainBus,
			additionalPersistentSubscriptionConsumerStrategyFactories);
		var persistentSubscription = new PersistentSubscriptionService<TStreamId>(perSubscrQueue, readIndex, psubDispatcher,
			_mainQueue, consumerStrategyRegistry, trackers.PersistentSubscriptionTracker);
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
		var scavengerDispatcher = new IODispatcher(_mainQueue, _mainQueue);
		_mainBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(scavengerDispatcher.BackwardReader);
		_mainBus.Subscribe<ClientMessage.NotHandled>(scavengerDispatcher.BackwardReader);
		_mainBus.Subscribe<ClientMessage.WriteEventsCompleted>(scavengerDispatcher.Writer);
		_mainBus.Subscribe<IODispatcherDelayedMessage>(scavengerDispatcher);
		_mainBus.Subscribe<ClientMessage.NotHandled>(scavengerDispatcher);

		// reuse the same buffer; it's quite big.
		var calculatorBuffer = new Calculator<TStreamId>.Buffer(32_768);

		scavengerFactory = new ScavengerFactory((message, scavengerLogger, logger) => {
			// currently on the main queue
			var optionsCalculator = new ScavengeOptionsCalculator(options, archiveOptions, message);

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
				chunkSize: options.Database.ChunkSize,
				metastreamLookup: logFormat.Metastreams,
				chunkReader: new ChunkReaderForAccumulator<TStreamId>(
					Db.Manager,
					logFormat.Metastreams,
					logFormat.StreamIdConverter,
					Db.Config.ReplicationCheckpoint.AsReadOnly(),
					options.Database.ChunkSize),
				index: new IndexReaderForAccumulator<TStreamId>(readIndex),
				cancellationCheckPeriod: cancellationCheckPeriod,
				throttle: throttle);

			var calculator = new Calculator<TStreamId>(
				logger: logger,
				new IndexReaderForCalculator<TStreamId>(
					readIndex,
					() => new TFReaderLease(readerPool),
					state.LookupUniqueHashUser),
				chunkSize: options.Database.ChunkSize,
				cancellationCheckPeriod: cancellationCheckPeriod,
				buffer: calculatorBuffer,
				throttle: throttle);

			var chunkRemover = IChunkRemover<TStreamId, ILogRecord>.NoOp;
			if (archiveOptions.Enabled) {
				chunkRemover = new ChunkRemover<TStreamId, ILogRecord>(
					logger: logger,
					archiveCheckpoint: new AdvancingCheckpoint(archiveReader.GetCheckpoint),
					chunkManager: new ChunkManagerForChunkRemover(Db.Manager),
					locatorCodec: locatorCodec,
					retainPeriod: TimeSpan.FromDays(archiveOptions.RetainAtLeast.Days),
					retainBytes: archiveOptions.RetainAtLeast.LogicalBytes);
			}

			var chunkExecutor = new ChunkExecutor<TStreamId, ILogRecord>(
				logger,
				logFormat.Metastreams,
				chunkRemover,
				new ChunkManagerForExecutor<TStreamId>(logger, Db.Manager, Db.Config, Db.TransformManager),
				chunkSize: Db.Config.ChunkSize,
				unsafeIgnoreHardDeletes: options.Database.UnsafeIgnoreHardDelete,
				cancellationCheckPeriod: cancellationCheckPeriod,
				threads: message.Threads,
				isArchiver: options.Cluster.Archiver,
				throttle: throttle);

			var chunkMerger = new ChunkMerger(
				logger: logger,
				mergeChunks: optionsCalculator.MergeChunks,
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
				thresholdForNewScavenge: optionsCalculator.ChunkExecutionThreshold,
				syncOnly: message.SyncOnly,
				getThrottleStats: () => throttle.PrettyPrint());
		});

		var scavengerLogManager = new TFChunkScavengerLogManager(
			nodeEndpoint: $"{GossipAdvertiseInfo.HttpEndPoint.Host}:{GossipAdvertiseInfo.HttpEndPoint.Port}",
			scavengeHistoryMaxAge: TimeSpan.FromDays(options.Database.ScavengeHistoryMaxAge),
			ioDispatcher: scavengerDispatcher);

		var storageScavenger = new StorageScavenger(
			logManager: scavengerLogManager,
			scavengerFactory: scavengerFactory,
			switchChunksLock: _switchChunksLock);

		// ReSharper disable RedundantTypeArgumentsOfMethod
		_mainBus.Subscribe<ClientMessage.ScavengeDatabase>(storageScavenger);
		_mainBus.Subscribe<ClientMessage.StopDatabaseScavenge>(storageScavenger);
		_mainBus.Subscribe<ClientMessage.GetCurrentDatabaseScavenge>(storageScavenger);
		_mainBus.Subscribe<ClientMessage.GetLastDatabaseScavenge>(storageScavenger);
		_mainBus.Subscribe<SystemMessage.StateChangeMessage>(storageScavenger);
		// ReSharper restore RedundantTypeArgumentsOfMethod

		// REDACTION
		var redactionBus = new InMemoryBus("RedactionBus", true, TimeSpan.FromSeconds(2));
		var redactionQueue = new QueuedHandlerThreadPool(redactionBus, "Redaction", _queueStatsManager,
			trackers.QueueTrackers, false);

		_mainBus.Subscribe<RedactionMessage.GetEventPosition>(redactionQueue);
		_mainBus.Subscribe<RedactionMessage.AcquireChunksLock>(redactionQueue);
		_mainBus.Subscribe<RedactionMessage.SwitchChunk>(redactionQueue);
		_mainBus.Subscribe<RedactionMessage.ReleaseChunksLock>(redactionQueue);
		_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(redactionQueue);

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

		// ELECTIONS TRACKER
		_mainBus.Subscribe<ElectionMessage.ElectionsDone>(trackers.ElectionCounterTracker);

		// TELEMETRY
		var telemetryService = new TelemetryService(
			Db.Manager,
			modifiedOptions,
			configuration,
			_mainQueue,
			new TelemetrySink(options.Application.TelemetryOptout),
			Db.Config.WriterCheckpoint.AsReadOnly(),
			memberInfo.InstanceId
		);
		if (modifiedOptions.Cluster.ReadOnlyReplica)
			_mainBus.Subscribe<SystemMessage.ReplicaStateMessage>(telemetryService);
		_mainBus.Subscribe<SystemMessage.StateChangeMessage>(telemetryService);
		_mainBus.Subscribe<ElectionMessage.ElectionsDone>(telemetryService);
		_mainBus.Subscribe<LeaderDiscoveryMessage.LeaderFound>(telemetryService);

		// LEADER REPLICATION
		var leaderReplicationService = new LeaderReplicationService(_mainQueue, NodeInfo.InstanceId, Db,
			_workersHandler,
			epochManager, options.Cluster.ClusterSize,
			options.Cluster.UnsafeAllowSurplusNodes,
			_queueStatsManager);
		AddTask(leaderReplicationService.Task);

		if (!isSingleNode) {
			_mainBus.Subscribe<SystemMessage.SystemStart>(leaderReplicationService);
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(leaderReplicationService);
			_mainBus.Subscribe<SystemMessage.EnablePreLeaderReplication>(leaderReplicationService);
			_mainBus.Subscribe<ReplicationMessage.ReplicaSubscriptionRequest>(leaderReplicationService);
			_mainBus.Subscribe<ReplicationMessage.ReplicaLogPositionAck>(leaderReplicationService);
			_mainBus.Subscribe<ReplicationTrackingMessage.ReplicatedTo>(leaderReplicationService);
			monitoringInnerBus.Subscribe<ReplicationMessage.GetReplicationStats>(leaderReplicationService);

			// REPLICA REPLICATION
			var replicaService = new ReplicaService(_mainQueue, Db, epochManager, _workersHandler,
				_authenticationProvider, authorizationGateway,
				GossipAdvertiseInfo.InternalTcp ?? GossipAdvertiseInfo.InternalSecureTcp,
				options.Cluster.ReadOnlyReplica,
				!disableInternalTcpTls, _internalServerCertificateValidator,
				_certificateSelector,
				TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatTimeout),
				TimeSpan.FromMilliseconds(options.Interface.ReplicationHeartbeatInterval),
				TimeSpan.FromMilliseconds(options.Database.WriteTimeoutMs));
			_mainBus.Subscribe<SystemMessage.StateChangeMessage>(replicaService);
			_mainBus.Subscribe<ReplicationMessage.ReconnectToLeader>(replicaService);
			_mainBus.Subscribe<ReplicationMessage.SubscribeToLeader>(replicaService);
			_mainBus.Subscribe<ReplicationMessage.AckLogPosition>(replicaService);
			_mainBus.Subscribe<ClientMessage.TcpForwardMessage>(replicaService);
		} else {
			//LeaderReplicationService only running on a single node to provide stats, hence not subscribed to the other message types like SystemStart and StateChangeMessage
			monitoringInnerBus.Subscribe<ReplicationMessage.GetReplicationStats>(leaderReplicationService);
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

		// GOSSIP

		var gossipSeedSource = (
			options.Cluster.DiscoverViaDns,
			options.Cluster.ClusterSize > 1,
			options.Cluster.GossipSeed is { Length: > 0 }) switch {
				(true, true, _) => (IGossipSeedSource)new DnsGossipSeedSource(options.Cluster.ClusterDns,
					options.Cluster.ClusterGossipPort),
				(false, true, false) => throw new InvalidConfigurationException(
					"DNS discovery is disabled, but no gossip seed endpoints have been specified. "
					+ "Specify gossip seeds using the `GossipSeed` option."),
				_ => new KnownEndpointGossipSeedSource(options.Cluster.GossipSeed)
			};

		var gossip = new NodeGossipService(
			_mainQueue,
			options.Cluster.ClusterSize,
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

		var clusterStateChangeListener = new ClusterMultipleVersionsLogger();
		_mainBus.Subscribe<GossipMessage.GossipUpdated>(clusterStateChangeListener);

		_reloadConfigSignalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => {
			c.Cancel = true;
			Log.Information("Reloading the node's configuration since {Signal} has been received.", c.Signal);
			_mainQueue.Publish(new ClientMessage.ReloadConfig());
		});

		// subsystems
		_subsystems = options.Subsystems;

		var standardComponents = new StandardComponents(Db.Config, _mainQueue, _mainBus, _timerService, _timeProvider,
			httpSendService, new IHttpService[] { _httpService }, _workersHandler, _queueStatsManager, trackers.QueueTrackers, metricsConfiguration);

		IServiceCollection ConfigureNodeServices(IServiceCollection services) {
			services
				.AddSingleton(telemetryService) // for correct disposal
				.AddSingleton(_readIndex)
				.AddSingleton(standardComponents)
				.AddSingleton(authorizationGateway)
				.AddSingleton(certificateProvider)
				.AddSingleton(_authenticationProvider)
				.AddSingleton<IReadOnlyList<IDbTransform>>(new List<IDbTransform> { new IdentityDbTransform() })
				.AddSingleton<IReadOnlyList<IClusterVNodeStartupTask>>(new List<IClusterVNodeStartupTask> { })
				.AddSingleton<IReadOnlyList<IHttpAuthenticationProvider>>(httpAuthenticationProviders)
				.AddSingleton<Func<(X509Certificate2 Node, X509Certificate2Collection Intermediates,
						X509Certificate2Collection Roots)>>
					(() => (_certificateSelector(), _intermediateCertsSelector(), _trustedRootCertsSelector()))
				.AddSingleton(_nodeHttpClientFactory)
				.AddSingleton<IChunkRegistry<IChunkBlob>>(Db.Manager)
				.AddSingleton<IVersionedFileNamingStrategy>(Db.Manager.FileSystem.LocalNamingStrategy)
				.AddSingleton(dbConfig);

			configureAdditionalNodeServices?.Invoke(services);
			return services;
		}

		IReadOnlyList<IClusterVNodeStartupTask> startupTasks = [];

		void ConfigureNode(IApplicationBuilder app) {
			var dbTransforms = app.ApplicationServices.GetService<IReadOnlyList<IDbTransform>>();
			Db.TransformManager.LoadTransforms(dbTransforms);

			if (!Db.TransformManager.TrySetActiveTransform(options.Database.Transform))
				throw new InvalidConfigurationException(
					$"Unknown {nameof(options.Database.Transform)} specified: {options.Database.Transform}");

			startupTasks = app.ApplicationServices.GetRequiredService<IReadOnlyList<IClusterVNodeStartupTask>>();
		}

		async ValueTask StartNode(CancellationToken token) {
			// TRUNCATE IF NECESSARY
			var truncPos = Db.Config.TruncateCheckpoint.Read();
			if (truncPos != -1) {
				Log.Information(
					"Truncate checkpoint is present. Truncate: {truncatePosition} (0x{truncatePosition:X}), Writer: {writerCheckpoint} (0x{writerCheckpoint:X}), Chaser: {chaserCheckpoint} (0x{chaserCheckpoint:X}), Epoch: {epochCheckpoint} (0x{epochCheckpoint:X})",
					truncPos, truncPos, writerCheckpoint, writerCheckpoint, chaserCheckpoint, chaserCheckpoint,
					epochCheckpoint, epochCheckpoint);
				var truncator = new TFChunkDbTruncator(Db.Config, Db.Manager.FileSystem, type => Db.TransformManager.GetFactoryForExistingChunk(type));
				await truncator.TruncateDb(truncPos, CancellationToken.None);

				// The truncator has moved the checkpoints but it is possible that other components in the startup have
				// already read the old values. If we ensure all checkpoint reads are performed after the truncation
				// then we can remove this extra restart
				Log.Information("Truncation successful. Shutting down.");
				var shutdownGuid = Guid.NewGuid();
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
				linked.CancelAfter(ShutdownTimeout);

				await HandleAsync(
						new SystemMessage.BecomeShuttingDown(shutdownGuid, exitProcess: true, shutdownHttp: true),
						linked.Token);

				Handle(new SystemMessage.BecomeShutdown(shutdownGuid));
				Application.Exit(0, "Shutting down after successful truncation.");
				return;
			}

			foreach (var x in startupTasks) {
				await x.Run(token);
			}

			// start the main queue as we publish messages to it while opening the db
			_controller.Start();

			await Db.Open(!options.Database.SkipDbVerify, threads: options.Database.InitializationThreads,
				createNewChunks: false, token: token);

			await epochManager.Init(token);

			await storageWriter.Start(token);

			_workersHandler.Start();
			monitoringQueue.Start();
			subscrQueue.Start();
			perSubscrQueue.Start();
			redactionQueue.Start();
			dynamicCacheManager.Start();
			_mainQueue.Publish(new SystemMessage.SystemInit());
		}
		_start = StartNode;

		_startup = new ClusterVNodeStartup<TStreamId>(
			options,
			modifiedOptions.PlugableComponents,
			_mainQueue, monitoringQueue, _mainBus, _workersHandler,
			_authenticationProvider, _authorizationProvider,
			expiryStrategy ?? new DefaultExpiryStrategy(),
			_httpService,
			configuration,
			trackers,
			ConfigureNodeServices,
			ConfigureNode);

		_mainBus.Subscribe<SystemMessage.SystemReady>(_startup);
		_mainBus.Subscribe<SystemMessage.BecomeShuttingDown>(_startup);

		var certificateExpiryMonitor = new CertificateExpiryMonitor(_mainQueue, _certificateSelector, Log);
		_mainBus.Subscribe<SystemMessage.SystemStart>(certificateExpiryMonitor);
		_mainBus.Subscribe<MonitoringMessage.CheckCertificateExpiry>(certificateExpiryMonitor);

		var periodicLogging = new PeriodicallyLoggingService(_mainQueue, VersionInfo.Version, Log);
		_mainBus.Subscribe<SystemMessage.SystemStart>(periodicLogging);
		_mainBus.Subscribe<MonitoringMessage.CheckEsVersion>(periodicLogging);
	}

	static int GetPTableMaxReaderCount(int readerThreadsCount) {
		var ptableMaxReaderCount =
			1 /* StorageWriter */
			+ 1 /* StorageChaser */
			+ 1 /* Projections */
			+ TFChunkScavenger.MaxThreadCount /* Scavenging (1 per thread) */
			+ 1 /* Redaction */
			+ 1 /* Subscription LinkTos resolving */
			+ readerThreadsCount
			+ 5 /* just in case reserve :) */;
		return Math.Max(ptableMaxReaderCount, ESConsts.PTableInitialReaderCount);
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

	public override async Task StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
		if (Interlocked.Exchange(ref _stopCalled, 1) == 1) {
			Log.Warning("Stop was already called.");
			return;
		}

		_mainQueue.Publish(new ClientMessage.RequestShutdown(false, true));

		try {
			await _shutdownSource.Task.WaitAsync(timeout ?? ShutdownTimeout, cancellationToken);
		} catch (Exception) {
			Log.Error("Graceful shutdown not complete. Forcing shutdown now.");
			throw;
		} finally {
			_switchChunksLock?.Dispose();
		}
	}

	public async ValueTask HandleAsync(SystemMessage.BecomeShuttingDown message, CancellationToken token) {
		Log.Information("========== [{httpEndPoint}] IS SHUTTING DOWN SUBSYSTEMS...", NodeInfo.HttpEndPoint);

		_reloadConfigSignalRegistration?.Dispose();
		_reloadConfigSignalRegistration = null;

		foreach (var subsystem in _subsystems ?? [])
			await subsystem.Stop().WaitAsync(token);
	}

	public void Handle(SystemMessage.BecomeShutdown message) {
		_shutdownSource.TrySetResult(true);
	}

	public void Handle(SystemMessage.SystemStart _) {
		_authenticationProvider.Initialize().ContinueWith(t => {
			Message msg = t.Exception is null
				? new AuthenticationMessage.AuthenticationProviderInitialized()
				: new AuthenticationMessage.AuthenticationProviderInitializationFailed();

			_mainQueue.Publish(msg);
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

	public override async Task<ClusterVNode> StartAsync(bool waitUntilReady, CancellationToken token) {
		var tcs = new TaskCompletionSource<ClusterVNode>(TaskCreationOptions.RunContinuationsAsynchronously);

		if (waitUntilReady) {
			_mainBus.Subscribe(new AdHocHandler<SystemMessage.SystemReady>(
				_ => tcs.TrySetResult(this)));
		} else {
			tcs.TrySetResult(this);
		}

		await _start(token);

		if (IsShutdown)
			tcs.TrySetResult(this);

		return await tcs.Task;
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
				ReloadTransform(options);
				Log.Information("The node's configuration was successfully reloaded");
			} catch (Exception exc) {
				Log.Error(exc, "An error has occurred while reloading the configuration");
			} finally {
				Interlocked.Exchange(ref _reloadingConfig, 0);
			}
		});
	}

	private void ReloadTransform(ClusterVNodeOptions options) {
		var transform = options.Database.Transform;
		if (!Db.TransformManager.TrySetActiveTransform(transform))
			Log.Error($"Unknown {nameof(options.Database.Transform)} specified: {options.Database.Transform}");
	}

	private void ReloadLogOptions(ClusterVNodeOptions options) {
		if (options.Logging.LogLevel != LogLevel.Default) {
			var changed = EventStoreLoggerConfiguration.AdjustMinimumLogLevel(options.Logging.LogLevel);
			if (changed) {
				Log.Information($"The log level was adjusted to: {options.Logging.LogLevel}");

				if (options.Logging.LogLevel > LogLevel.Information) {
					Console.WriteLine($"The log level was adjusted to: {options.Logging.LogLevel}");
				}
			}
		}
	}

	private void ReloadCertificates(ClusterVNodeOptions options) {
		if (options.Application.Insecure) {
			Log.Information("Skipping reload of certificates since TLS is disabled.");
			return;
		}

		if (_certificateProvider?.LoadCertificates(options) == LoadCertificateResult.VerificationFailed) {
			throw new InvalidConfigurationException("Aborting certificate loading due to verification errors.");
		}
	}

	private static void LogPluginSubsectionWarnings(IConfiguration configuration) {
		var pluginSubsectionOptions = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:Plugins").AsEnumerable().ToList();
		if (pluginSubsectionOptions.Count <= 1)
			return;

		Log.Warning(
			"The \"Plugins\" configuration subsection has been removed. " +
			"The following settings will be ignored. " +
			"Please move them out of the \"Plugins\" subsection and " +
			$"directly into the \"{KurrentConfigurationKeys.Prefix}\" root.");

		foreach (var kvp in pluginSubsectionOptions) {
			if (kvp.Value is not null)
				Log.Warning("Ignoring option nested in \"Plugins\" subsection: {IgnoredOption}", kvp.Key);
		}
	}

	public override string ToString() =>
		$"[{NodeInfo.InstanceId:B}, {NodeInfo.InternalTcp}, {NodeInfo.ExternalTcp}, {NodeInfo.HttpEndPoint}]";
}
