// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using EventStore.Common.Utils;
using EventStore.Core.Services.Monitoring;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Http;
using EventStore.Core.Tests.Services.Transport.Tcp;
using EventStore.Core.TransactionLog.Chunks;
using System.Threading.Tasks;
using EventStore.Core.Authentication;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Authorization;
using EventStore.Core.Authorization.AuthorizationPolicies;
using EventStore.Core.Bus;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using ILogger = Serilog.ILogger;
using EventStore.Plugins.Subsystems;
using EventStore.TcpUnitTestPlugin;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using EventStore.Plugins.Authentication;
using EventStore.Plugins.Authorization;
using EventStore.Plugins.Transforms;
using Microsoft.Extensions.DependencyInjection;
using RuntimeInformation = System.Runtime.RuntimeInformation;
using EventStore.Core.Tests.Index.Hashers;
using System.Threading;
using Serilog;

namespace EventStore.Core.Tests.Helpers;

public class MiniNode {
	public const int ChunkSize = 1024 * 1024;
	public const int CachedChunkSize = ChunkSize + ChunkHeader.Size + ChunkFooter.Size;

	protected static readonly ILogger Log = Serilog.Log.ForContext<MiniNode>();
	public IPEndPoint TcpEndPoint { get; protected set; }
	public IPEndPoint IntTcpEndPoint { get; protected set; }
	public IPEndPoint HttpEndPoint { get; protected set; }
}

public class MiniNode<TLogFormat, TStreamId> : MiniNode, IAsyncDisposable {
	public static int RunCount;
	public static readonly Stopwatch RunningTime = new Stopwatch();
	public static readonly Stopwatch StartingTime = new Stopwatch();
	public static readonly Stopwatch StoppingTime = new Stopwatch();

	public readonly ClusterVNode Node;
	public readonly TFChunkDb Db;
	public readonly string DbPath;
	public HttpClient HttpClient;
	public HttpMessageHandler HttpMessageHandler;

	private readonly WebApplication _webHost;
	private readonly TaskCompletionSource<bool> _started;
	private readonly TaskCompletionSource<bool> _adminUserCreated;
	private readonly int _httpClientTimeoutSec;
	private bool _testServerStarted;
	public Task Started => _started.Task;
	public Task AdminUserCreated => _adminUserCreated.Task;

	public MiniNode(string pathname,
		int? tcpPort = null, int? httpPort = null,
		ISubsystem[] subsystems = null,
		int chunkSize = ChunkSize, int cachedChunkSize = CachedChunkSize, bool enableTrustedAuth = false,
		int memTableSize = 1000,
		bool inMemDb = true, bool disableFlushToDisk = false,
		string advertisedExtHostAddress = null, int advertisedHttpPort = 0,
		int hashCollisionReadLimit = Util.Opts.HashCollisionReadLimitDefault,
		byte indexBitnessVersion = Util.Opts.IndexBitnessVersionDefault,
		bool hash32bit = false,
		string dbPath = "", bool isReadOnlyReplica = false,
		long streamExistenceFilterSize = 10_000,
		int streamExistenceFilterCheckpointIntervalMs = 30_000,
		int streamExistenceFilterCheckpointDelayMs = 5_000,
		int httpClientTimeoutSec = 60,
		IAuthenticationProviderFactory authenticationProviderFactory = null,
		IAuthorizationProviderFactory authorizationProviderFactory = null,
		IExpiryStrategy expiryStrategy = null,
		string transform = "identity",
		IConfiguration configuration = null,
		IReadOnlyList<IDbTransform> newTransforms = null,
		int maxAppendEventSize = TFConsts.EffectiveMaxLogRecordSize) {

		_httpClientTimeoutSec = httpClientTimeoutSec;
		RunningTime.Start();
		RunCount += 1;

		var ip = IPAddress.Loopback;

		int extTcpPort = tcpPort ?? PortsHelper.GetAvailablePort(ip);
		int httpEndPointPort = httpPort ?? PortsHelper.GetAvailablePort(ip);
		int intTcpPort = PortsHelper.GetAvailablePort(ip);

		if (string.IsNullOrEmpty(dbPath)) {
			DbPath = Path.Combine(pathname,
				$"mini-node-db-{extTcpPort}-{httpEndPointPort}");
		} else {
			DbPath = dbPath;
		}

		TcpEndPoint = new IPEndPoint(ip, extTcpPort);
		IntTcpEndPoint = new IPEndPoint(ip, intTcpPort);
		HttpEndPoint = new IPEndPoint(ip, httpEndPointPort);

		subsystems ??= [];
		subsystems = [..subsystems, new TcpApiTestPlugin()];

		var options = new ClusterVNodeOptions {
				IndexBitnessVersion = indexBitnessVersion,
				Application = new() {
					AllowAnonymousEndpointAccess = true,
					AllowAnonymousStreamAccess = true,
					StatsPeriodSec = 60 * 60,
					WorkerThreads = 1,
					MaxAppendEventSize = maxAppendEventSize
				},
				Interface = new() {
					ReplicationHeartbeatInterval = 10_000,
					ReplicationHeartbeatTimeout = 10_000,
					EnableTrustedAuth = enableTrustedAuth,
					EnableAtomPubOverHttp = true
				},
				Cluster = new() {
					DiscoverViaDns = false,
					ReadOnlyReplica = isReadOnlyReplica,
					Archiver = false,
					StreamInfoCacheCapacity = 10_000
				},
				Database = new() {
					ChunkSize = chunkSize,
					ChunksCacheSize = cachedChunkSize,
					SkipDbVerify = true,
					StatsStorage = StatsStorage.None,
					MaxMemTableSize = memTableSize,
					DisableScavengeMerging = true,
					HashCollisionReadLimit = hashCollisionReadLimit,
					CommitTimeoutMs = 10_000,
					PrepareTimeoutMs = 10_000,
					UnsafeDisableFlushToDisk = disableFlushToDisk,
					StreamExistenceFilterSize = streamExistenceFilterSize,
					Transform = transform
				},
				PlugableComponents = subsystems,
				// limitation: the LoadedOptions here will only reflect the defaults and not the rest
				// of the config specified above. however we only use it for /info/options
				LoadedOptions = ClusterVNodeOptions.GetLoadedOptions(new ConfigurationBuilder()
					.AddKurrentDefaultValues()
					.Build()),
			}.Secure(new X509Certificate2Collection(ssl_connections.GetRootCertificate()),
				ssl_connections.GetServerCertificate())
			.WithReplicationEndpointOn(IntTcpEndPoint)
			.WithExternalTcpOn(TcpEndPoint)
			.WithNodeEndpointOn(HttpEndPoint);

		var configurationBuilder = new ConfigurationBuilder()
			.AddInMemoryCollection([
				new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:NodeTcpPort", extTcpPort.ToString()),
				new($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp", "true"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeTcpPort", extTcpPort.ToString()),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeHeartbeatInterval", "10000"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:NodeHeartbeatTimeout", "10000"),
				new($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin:Insecure", options.Application.Insecure.ToString()),
			]);

		if (configuration is not null)
			configurationBuilder.AddConfiguration(configuration);

		var inMemConf = configurationBuilder.Build();

		if (advertisedExtHostAddress != null)
			options = options.AdvertiseNodeAs(new DnsEndPoint(advertisedExtHostAddress, advertisedHttpPort));

		options = inMemDb
			? options.RunInMemory()
			: options.RunOnDisk(DbPath);

		Log.Information("\n{0,-25} {1} ({2}/{3}, {4})\n"
				 + "{5,-25} {6} ({7})\n"
				 + "{8,-25} {9} ({10}-bit)\n"
				 + "{11,-25} {12}\n"
				 + "{13,-25} {14}\n"
				 + "{15,-25} {16}\n"
				 + "{17,-25} {18}\n\n",
			"ES VERSION:", VersionInfo.Version, VersionInfo.Edition, VersionInfo.CommitSha, VersionInfo.Timestamp,
			"OS:", RuntimeInformation.OsPlatform, Environment.OSVersion,
			"RUNTIME:", RuntimeInformation.RuntimeVersion, RuntimeInformation.RuntimeMode,
			"GC:",
			GC.MaxGeneration == 0
				? "NON-GENERATION (PROBABLY BOEHM)"
				: $"{GC.MaxGeneration + 1} GENERATIONS",
			"DBPATH:", DbPath,
			"TCP ENDPOINT:", TcpEndPoint,
			"HTTP ENDPOINT:", HttpEndPoint);

		var logFormatFactory = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory
			.Configure(options => options with {
				StreamExistenceFilterCheckpointDelay = TimeSpan.FromMilliseconds(streamExistenceFilterCheckpointDelayMs),
				StreamExistenceFilterCheckpointInterval = TimeSpan.FromMilliseconds(streamExistenceFilterCheckpointIntervalMs),
				HighHasher = hash32bit ? new ConstantHasher(0) : options.HighHasher,
			});

		Node = new ClusterVNode<TStreamId>(options, logFormatFactory,
			new AuthenticationProviderFactory(
				c => authenticationProviderFactory ?? new InternalAuthenticationProviderFactory(
					c,
					options.DefaultUser)),
			new AuthorizationProviderFactory(
				c => authorizationProviderFactory ?? new InternalAuthorizationProviderFactory(
					new StaticAuthorizationPolicyRegistry([new LegacyPolicySelectorFactory(
						options.Application.AllowAnonymousEndpointAccess,
						options.Application.AllowAnonymousStreamAccess,
						options.Application.OverrideAnonymousEndpointAccessForGossip).Create(c.MainQueue)]))),
			expiryStrategy: expiryStrategy,
			certificateProvider: new OptionsCertificateProvider(),
			configuration: inMemConf,
			configureAdditionalNodeServices: services => ConfigureMiniNodeServices(services, newTransforms));
		Db = Node.Db;

		Node.HttpService.SetupController(new TestController(Node.MainQueue));
		var builder = WebApplication.CreateBuilder();
		builder.WebHost
			.ConfigureKestrel(o => {
				o.Listen(HttpEndPoint, options => {
					if (RuntimeInformation.IsOSX) {
						options.Protocols = HttpProtocols.Http2;
					} else {
						options.UseHttps(new HttpsConnectionAdapterOptions {
							ServerCertificate = ssl_connections.GetServerCertificate(),
							ClientCertificateMode = ClientCertificateMode.AllowCertificate,
							ClientCertificateValidation = (certificate, chain, sslPolicyErrors) => {
								var (isValid, error) =
									ClusterVNode<string>.ValidateClientCertificate(certificate, chain, sslPolicyErrors, () => null, () => new X509Certificate2Collection(ssl_connections.GetRootCertificate()));
								if (!isValid && error != null) {
									Log.Error("Client certificate validation error: {e}", error);
								}
								return isValid;
							}
						});
					}
				});
			})
			.UseTestServer();
		builder.Services.AddSerilog();
		Node.Startup.ConfigureServices(builder.Services);
		_webHost = builder.Build();
		Node.Startup.Configure(_webHost);
		_started = new TaskCompletionSource<bool>();
		_adminUserCreated = new TaskCompletionSource<bool>();
	}

	public async Task StartTestServer() {
		await _webHost.StartAsync();
		var testServer = _webHost.GetTestServer();
		HttpMessageHandler = testServer.CreateHandler();
		HttpClient = new HttpClient(HttpMessageHandler) {
			Timeout = TimeSpan.FromSeconds(_httpClientTimeoutSec),
			BaseAddress = new UriBuilder {
				Scheme = Uri.UriSchemeHttps
			}.Uri
		};
		_testServerStarted = true;
	}

	private static void ConfigureMiniNodeServices(
		IServiceCollection services,
		IReadOnlyList<IDbTransform> newTransforms) {
		if (newTransforms == null)
			return;

		services.Decorate<IReadOnlyList<IDbTransform>>(existingTransforms =>
			DecorateTransforms(existingTransforms, newTransforms));
	}

	private static IReadOnlyList<IDbTransform> DecorateTransforms(
		IReadOnlyList<IDbTransform> existingTransforms,
		IReadOnlyList<IDbTransform> newTransforms) {
		var transforms = existingTransforms.ToList();
		transforms.AddRange(newTransforms);
		return transforms;
	}

	public async Task Start() {
		if (!_testServerStarted) {
			await StartTestServer();
		}

		StartingTime.Start();
		Node.MainBus.Subscribe(
			new AdHocHandler<SystemMessage.BecomeLeader>(m => {
				_started.TrySetResult(true);
			}));

		AdHocHandler<StorageMessage.EventCommitted> waitForAdminUser = null;
		waitForAdminUser = new AdHocHandler<StorageMessage.EventCommitted>(WaitForAdminUser);
		Node.MainBus.Subscribe(waitForAdminUser);

		void WaitForAdminUser(StorageMessage.EventCommitted m) {
			if (m.Event.EventStreamId != "$user-admin") {
				return;
			}

			_adminUserCreated.TrySetResult(true);
			Node.MainBus.Unsubscribe(waitForAdminUser);
		}

		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromSeconds(60));
		await Node.StartAsync(true, cts.Token);

		if (Node.IsShutdown)
			_started.TrySetResult(true);

		await Started.WithTimeout();

		StartingTime.Stop();
		Log.Information("MiniNode successfully started!");
	}

	public async Task Shutdown(bool keepDb = false) {

		StoppingTime.Start();

		// _kestrelTestServer.Dispose();
		HttpMessageHandler.Dispose();
		HttpClient.Dispose();
		await Node.StopAsync(TimeSpan.FromSeconds(20));
		await _webHost.DisposeAsync();

		if (!keepDb)
			TryDeleteDirectory(DbPath);

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

	public async ValueTask DisposeAsync() {
		await Shutdown();
	}
}
