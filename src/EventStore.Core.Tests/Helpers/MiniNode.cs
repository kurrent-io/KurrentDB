using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
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
using EventStore.Core.Bus;
using EventStore.Core.Certificates;
using EventStore.Core.Messages;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using ILogger = Serilog.ILogger;
using EventStore.Core.LogAbstraction;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace EventStore.Core.Tests.Helpers {
	public class MiniNode {
		public const int ChunkSize = 1024 * 1024;
		public const int CachedChunkSize = ChunkSize + ChunkHeader.Size + ChunkFooter.Size;
		
		protected static readonly ILogger Log = Serilog.Log.ForContext<MiniNode>();
		public IPEndPoint TcpEndPoint { get; protected set; }
		public IPEndPoint IntTcpEndPoint { get; protected set; }
		public IPEndPoint HttpEndPoint { get; protected set; }
	}

	public class MiniNode<TLogFormat, TStreamId> : MiniNode {
		public static int RunCount;
		public static readonly Stopwatch RunningTime = new Stopwatch();
		public static readonly Stopwatch StartingTime = new Stopwatch();
		public static readonly Stopwatch StoppingTime = new Stopwatch();

		public readonly ClusterVNode Node;
		public readonly TFChunkDb Db;
		public readonly string DbPath;
		public readonly HttpClient HttpClient;
		private readonly IWebHost _host;
		public readonly HttpMessageHandler HttpMessageHandler;

		private readonly TestServer _kestrelTestServer;
		private readonly TaskCompletionSource<bool> _started;
		private readonly TaskCompletionSource<bool> _adminUserCreated;

		public Task Started => _started.Task;
		public Task AdminUserCreated => _adminUserCreated.Task;

		public MiniNode(string pathname,
			int? tcpPort = null, int? httpPort = null,
			ISubsystemFactory[] subsystems = null,
			int chunkSize = ChunkSize, int cachedChunkSize = CachedChunkSize, bool enableTrustedAuth = false,
			int memTableSize = 1000,
			bool inMemDb = true, bool disableFlushToDisk = false,
			string advertisedExtHostAddress = null, int advertisedHttpPort = 0,
			int hashCollisionReadLimit = Util.Opts.HashCollisionReadLimitDefault,
			byte indexBitnessVersion = Util.Opts.IndexBitnessVersionDefault,
			string dbPath = "", bool isReadOnlyReplica = false,
			long streamExistenceFilterSize = 10_000,
			int streamExistenceFilterCheckpointIntervalMs = 30_000,
			int streamExistenceFilterCheckpointDelayMs = 5_000,
			IExpiryStrategy expiryStrategy = null) {
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

			var options = new ClusterVNodeOptions {
					IndexBitnessVersion = indexBitnessVersion,
					Application = new() {
						AllowAnonymousEndpointAccess = true,
						AllowAnonymousStreamAccess = true,
						StatsPeriodSec = 60 * 60,
						WorkerThreads = 1
					},
					Interface = new() {
						NodeHeartbeatInterval = 10_000,
						NodeHeartbeatTimeout = 10_000,
						ReplicationHeartbeatInterval = 10_000,
						ReplicationHeartbeatTimeout = 10_000,
						EnableTrustedAuth = enableTrustedAuth,
						EnableAtomPubOverHttp = true
					},
					Cluster = new() {
						DiscoverViaDns = false,
						ReadOnlyReplica = isReadOnlyReplica,
						StreamInfoCacheCapacity = 10_000
					},
					Database = new() {
						ChunkSize = chunkSize,
						ChunksCacheSize = cachedChunkSize,
						ChunkCachePooling = false,
						SkipDbVerify = true,
						StatsStorage = StatsStorage.None,
						MaxMemTableSize = memTableSize,
						DisableScavengeMerging = true,
						HashCollisionReadLimit = hashCollisionReadLimit,
						CommitTimeoutMs = 10_000,
						PrepareTimeoutMs = 10_000,
						UnsafeDisableFlushToDisk = disableFlushToDisk,
						StreamExistenceFilterSize = streamExistenceFilterSize,
					},
					Subsystems = new List<ISubsystemFactory>(subsystems ?? Array.Empty<ISubsystemFactory>())
				}.Secure(new X509Certificate2Collection(ssl_connections.GetRootCertificate()),
					ssl_connections.GetServerCertificate())
				.WithInternalSecureTcpOn(IntTcpEndPoint)
				.WithExternalSecureTcpOn(TcpEndPoint)
				.WithHttpOn(HttpEndPoint);

			if (advertisedExtHostAddress != null)
				options = options.AdvertiseHttpHostAs(new DnsEndPoint(advertisedExtHostAddress, advertisedHttpPort));

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
				"ES VERSION:", VersionInfo.Version, VersionInfo.Tag, VersionInfo.Hashtag, VersionInfo.Timestamp,
				"OS:", OS.OsFlavor, Environment.OSVersion,
				"RUNTIME:", OS.GetRuntimeVersion(), Marshal.SizeOf(typeof(IntPtr)) * 8,
				"GC:",
				GC.MaxGeneration == 0
					? "NON-GENERATION (PROBABLY BOEHM)"
					: $"{GC.MaxGeneration + 1} GENERATIONS",
				"DBPATH:", DbPath,
				"TCP ENDPOINT:", TcpEndPoint,
				"HTTP ENDPOINT:", HttpEndPoint);

			var logFormatFactory = LogFormatHelper<TLogFormat, TStreamId>.LogFormatFactory
				.Wrap(x => new AbstractorFactoryTimingDecorator<TStreamId>(
					wrapped: x,
					streamExistenceFilterCheckpointIntervalMs: streamExistenceFilterCheckpointIntervalMs,
					streamExistenceFilterCheckpointDelayMs: streamExistenceFilterCheckpointDelayMs));
			Node = new ClusterVNode<TStreamId>(options, logFormatFactory,
				new AuthenticationProviderFactory(
					c => new InternalAuthenticationProviderFactory(c, options.DefaultUser)),
				new AuthorizationProviderFactory(c => new LegacyAuthorizationProviderFactory(c.MainQueue,
					options.Application.AllowAnonymousEndpointAccess,
					options.Application.AllowAnonymousStreamAccess,
					options.Application.OverrideAnonymousEndpointAccessForGossip)),
				metricsConfiguration: null,
				expiryStrategy: expiryStrategy,
				certificateProvider: new OptionsCertificateProvider());
			Db = Node.Db;

			Node.HttpService.SetupController(new TestController(Node.MainQueue));
			_kestrelTestServer = new TestServer(new WebHostBuilder()
				.UseKestrel()
				.UseStartup(Node.Startup));
			_started = new TaskCompletionSource<bool>();
			_adminUserCreated = new TaskCompletionSource<bool>();
			HttpMessageHandler = _kestrelTestServer.CreateHandler();
			HttpClient = new HttpClient(HttpMessageHandler) {
				BaseAddress = new UriBuilder {
					Scheme = Uri.UriSchemeHttps
				}.Uri
			};

			_host = new WebHostBuilder()
				.UseKestrel(o => {
					o.Listen(HttpEndPoint, options => {
						if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
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
				.UseStartup(Node.Startup)
				.Build();
		}

		public async Task Start() {
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

			await _host.StartAsync().WithTimeout();
			await Node.StartAsync(true).WithTimeout(TimeSpan.FromSeconds(60))
				.ConfigureAwait(false); //starts the node

			await Started.WithTimeout();
			
			StartingTime.Stop();
			Log.Information("MiniNode successfully started!");
		}

		public async Task Shutdown(bool keepDb = false) {

			StoppingTime.Start();

			_host?.Dispose();
			_kestrelTestServer.Dispose();
			HttpMessageHandler.Dispose();
			HttpClient.Dispose();
			await Node.StopAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

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
	}
}
