// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Messages;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Tests.Http;
using EventStore.Core.Tests.Services.Transport.Tcp;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.Data;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ILogger = Serilog.ILogger;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Plugins.Subsystems;
using EventStore.TcpUnitTestPlugin;
using Microsoft.Extensions.Configuration;
using RuntimeInformation = System.Runtime.RuntimeInformation;
using System.Threading;

namespace EventStore.Core.Tests.Helpers;

public class MiniClusterNode<TLogFormat, TStreamId> {
	public static int RunCount = 0;
	public static readonly Stopwatch RunningTime = new Stopwatch();
	public static readonly Stopwatch StartingTime = new Stopwatch();
	public static readonly Stopwatch StoppingTime = new Stopwatch();

	private static readonly ILogger Log = Serilog.Log.ForContext<MiniClusterNode<TLogFormat, TStreamId>>();

	public IPEndPoint InternalTcpEndPoint { get; }
	public IPEndPoint ExternalTcpEndPoint { get; }
	public IPEndPoint HttpEndPoint { get; }

	public readonly int DebugIndex;

	public readonly ClusterVNode Node;
	public TFChunkDb Db => Node.Db;
	private readonly string _dbPath;
	private readonly bool _isReadOnlyReplica;
	private readonly TaskCompletionSource<bool> _started = new();
	private readonly TaskCompletionSource<bool> _adminUserCreated = new();

	public Task Started => _started.Task;
	public Task AdminUserCreated => _adminUserCreated.Task;

	public VNodeState NodeState = VNodeState.Unknown;
	private readonly WebApplication _host;

	private static bool EnableHttps() => !RuntimeInformation.IsOSX;

        public MiniClusterNode(string pathname, int debugIndex, IPEndPoint internalTcp, IPEndPoint externalTcp,
		IPEndPoint httpEndPoint, EndPoint[] gossipSeeds, ISubsystem[] subsystems = null,
		bool enableTrustedAuth = false, int memTableSize = 1000, bool inMemDb = true,
		bool disableFlushToDisk = false, bool readOnlyReplica = false, int nodePriority = 0,
		string intHostAdvertiseAs = null, IExpiryStrategy expiryStrategy = null) {

		if (RuntimeInformation.IsOSX) {
			AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",
				true); //TODO JPB Remove this sadness when dotnet core supports kestrel + http2 on macOS
		}

		RunningTime.Start();
		RunCount += 1;

		DebugIndex = debugIndex;
		InternalTcpEndPoint = internalTcp;
		ExternalTcpEndPoint = externalTcp;
		HttpEndPoint = httpEndPoint;

		_dbPath = Path.Combine(
			pathname,
			$"mini-cluster-node-db-{externalTcp.Port}-{httpEndPoint.Port}");

		Directory.CreateDirectory(_dbPath);
		FileStreamExtensions.ConfigureFlush(disableFlushToDisk);

		var useHttps = EnableHttps();

		subsystems ??= [];
		subsystems = [..subsystems, new TcpApiTestPlugin()];

		var options = new ClusterVNodeOptions {
			Application = new() {
				AllowAnonymousEndpointAccess = true,
				AllowAnonymousStreamAccess = true,
				Insecure = !useHttps,
				WorkerThreads = 1,
				StatsPeriodSec = (int)TimeSpan.FromHours(1).TotalSeconds
			},
			Cluster = new() {
				DiscoverViaDns = false,
				ClusterDns = string.Empty,
				GossipSeed = gossipSeeds,
				ClusterSize = 3,
				NodePriority = nodePriority,
				GossipIntervalMs = 2_000,
				GossipAllowedDifferenceMs = 1_000,
				GossipTimeoutMs = 2_000,
				DeadMemberRemovalPeriodSec = 1_800_000,
				ReadOnlyReplica = readOnlyReplica,
				Archiver = false,
				StreamInfoCacheCapacity = 10_000
			},
			Interface = new() {
				ReplicationIp = InternalTcpEndPoint.Address,
				NodeIp = ExternalTcpEndPoint.Address,
				ReplicationPort = InternalTcpEndPoint.Port,
				NodePort = HttpEndPoint.Port,
				ReplicationHeartbeatTimeout = 2_000,
				ReplicationHeartbeatInterval = 2_000,
				EnableAtomPubOverHttp = true,
				EnableTrustedAuth = enableTrustedAuth,
				ReplicationHostAdvertiseAs = intHostAdvertiseAs
			},
			Database = new() {
				MinFlushDelayMs = TFConsts.MinFlushDelayMs.TotalMilliseconds,
				PrepareTimeoutMs = 10_000,
				CommitTimeoutMs = 10_000,
				WriteTimeoutMs = 10_000,
				StatsStorage = StatsStorage.None,
				DisableScavengeMerging = true,
				ScavengeHistoryMaxAge = 30,
				SkipDbVerify = true,
				MaxMemTableSize = memTableSize,
				MemDb = inMemDb,
				Db = _dbPath,
				ChunkSize = MiniNode.ChunkSize,
				ChunksCacheSize = MiniNode.CachedChunkSize,
				StreamExistenceFilterSize = 10_000
			},
			Projection = new() {
				RunProjections = ProjectionType.None
			},
			PlugableComponents = subsystems
		};

		var inMemConf = new ConfigurationBuilder()
			.AddInMemoryCollection(new KeyValuePair<string, string>[] {
				new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:NodeTcpPort", externalTcp.Port.ToString()),
				new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp", "true"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeTcpPort", externalTcp.Port.ToString()),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeHeartbeatInterval", "10000"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeHeartbeatTimeout", "10000"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:Insecure", options.Application.Insecure.ToString()),
			}).Build();
		var serverCertificate = useHttps ? ssl_connections.GetServerCertificate() : null;
		var trustedRootCertificates =
			useHttps ? new X509Certificate2Collection(ssl_connections.GetRootCertificate()) : null;
		options = useHttps
			? options.Secure(trustedRootCertificates, serverCertificate)
			: options;

		_isReadOnlyReplica = readOnlyReplica;

		Log.Information(
			"\n{0,-25} {1} ({2}/{3}, {4})\n" + "{5,-25} {6} ({7})\n" + "{8,-25} {9} ({10}-bit)\n"
			+ "{11,-25} {12}\n" + "{13,-25} {14}\n" + "{15,-25} {16}\n" + "{17,-25} {18}\n\n",
			"ES VERSION:", VersionInfo.Version, VersionInfo.Edition, VersionInfo.CommitSha, VersionInfo.Timestamp,
			"OS:", RuntimeInformation.OsPlatform, Environment.OSVersion, "RUNTIME:", RuntimeInformation.RuntimeVersion,
			RuntimeInformation.RuntimeMode, "GC:",
			GC.MaxGeneration == 0
				? "NON-GENERATION (PROBABLY BOEHM)"
				: $"{GC.MaxGeneration + 1} GENERATIONS", "DBPATH:", _dbPath, "ExTCP ENDPOINT:",
			ExternalTcpEndPoint, "ExHTTP ENDPOINT:", HttpEndPoint);

		var logFormatFactory = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory;
		Node = new ClusterVNode<TStreamId>(options, logFormatFactory, new AuthenticationProviderFactory(
				components =>
					new InternalAuthenticationProviderFactory(components, options.DefaultUser)),
			new AuthorizationProviderFactory(components =>
				new InternalAuthorizationProviderFactory(
					new StaticAuthorizationPolicyRegistry([new LegacyPolicySelectorFactory(
						options.Application.AllowAnonymousEndpointAccess,
						options.Application.AllowAnonymousStreamAccess,
						options.Application.OverrideAnonymousEndpointAccessForGossip).Create(components.MainQueue)]))),
			Array.Empty<IPersistentSubscriptionConsumerStrategyFactory>(),
			new OptionsCertificateProvider(),
			configuration: inMemConf,
			expiryStrategy,
			Guid.NewGuid(), debugIndex);
		Node.HttpService.SetupController(new TestController(Node.MainQueue));

		var builder = WebApplication.CreateBuilder();
		builder.WebHost
			.ConfigureKestrel(o => {
				o.Listen(HttpEndPoint, options => {
					if (RuntimeInformation.IsOSX) {
						options.Protocols = HttpProtocols.Http2;
					} else {
						options.UseHttps(new HttpsConnectionAdapterOptions {
							ServerCertificate = serverCertificate,
							ClientCertificateMode = ClientCertificateMode.AllowCertificate,
							ClientCertificateValidation = (certificate, chain, sslPolicyErrors) => {
								var (isValid, error) =
									ClusterVNode<string>.ValidateClientCertificate(certificate, chain, sslPolicyErrors,() => null,() => trustedRootCertificates);
								if (!isValid && error != null) {
									Log.Error("Client certificate validation error: {e}", error);
								}
								return isValid;
							}
						});
					}
				});
			});
		Node.Startup.ConfigureServices(builder.Services);
		_host = builder.Build();
		Node.Startup.Configure(_host);
	}

	public async Task Start() {
		StartingTime.Start();
		await _host.StartAsync();

		Node.MainBus.Subscribe(
			new AdHocHandler<SystemMessage.StateChangeMessage>(m => {
				NodeState = m.State;
			}));
		if (!_isReadOnlyReplica) {
			Node.MainBus.Subscribe(
				new AdHocHandler<SystemMessage.BecomeLeader>(m => {
					NodeState = VNodeState.Leader;
					_started.TrySetResult(true);
				}));
			Node.MainBus.Subscribe(
				new AdHocHandler<SystemMessage.BecomeFollower>(m => {
					NodeState = VNodeState.Follower;
					_started.TrySetResult(true);
				}));
		} else {
			Node.MainBus.Subscribe(
				new AdHocHandler<SystemMessage.BecomeReadOnlyReplica>(m => {
					NodeState = VNodeState.ReadOnlyReplica;
					_started.TrySetResult(true);
				}));
		}

		AdHocHandler<StorageMessage.EventCommitted> waitForAdminUser = null!;
		waitForAdminUser = new AdHocHandler<StorageMessage.EventCommitted>(WaitForAdminUser);
		Node.MainBus.Subscribe(waitForAdminUser);

		void WaitForAdminUser(StorageMessage.EventCommitted m) {
			if (m.Event.EventStreamId != "$user-admin") {
				return;
			}

			_adminUserCreated.TrySetResult(true);
			Node.MainBus.Unsubscribe(waitForAdminUser);
		}

		await Node.StartAsync(waitUntilReady: false, CancellationToken.None);
	}

	public HttpClient CreateHttpClient() {
		var httpClient = new HttpClient(new SocketsHttpHandler {
			AllowAutoRedirect = false,
			SslOptions = {
				RemoteCertificateValidationCallback = delegate { return true; }
			}
		}, true);

		var scheme = Node.DisableHttps ? "http://" : "https://";
		httpClient.BaseAddress = new Uri($"{scheme}{HttpEndPoint}");
		return httpClient;
	}

	public async Task Shutdown(bool keepDb = false) {
		StoppingTime.Start();
		if (_host != null) {
			await _host.DisposeAsync();
		}

		await Node.StopAsync().WithTimeout(TimeSpan.FromSeconds(20));

		// the same message 'BecomeShutdown' triggers the disposal of the ReadIndex
		// and also the notification here that the node as stopped so there is a race.
		// For now let's wait for a moment before we try to delete the directory.
		await Task.Delay(500);

		if (!keepDb)
			TryDeleteDirectory(_dbPath);

		StoppingTime.Stop();
		RunningTime.Stop();
	}

	public void WaitIdle() {
#if DEBUG
		Node.QueueStatsManager.WaitIdle();
#endif
	}

	private void TryDeleteDirectory(string directory) {
		try {
			Directory.Delete(directory, true);
		} catch (Exception e) {
			Debug.WriteLine("Failed to remove directory {0}", directory);
			Debug.WriteLine(e);
		}
	}
}
