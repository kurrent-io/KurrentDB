// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using Grpc.Net.Client;
using KurrentDB.Client;
using KurrentDB.Common.Exceptions;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KurrentDB.Embedded;

/// <summary>
/// A single-node KurrentDB server running inside the calling process, reachable over a UNIX domain socket.
/// </summary>
/// <remarks>
/// <para>
/// This is the same node the server executable runs — the same <c>ClusterVNode</c>, the same subsystems,
/// the same gRPC services — hosted differently. What embedding changes is how it is reached: clients
/// connect over a socket in the filesystem rather than over the network, and the server authenticates
/// those connections as the system account, so no credentials are needed and none are sent.
/// </para>
/// <para>
/// A node this size has nothing to gossip with and nothing to replicate to, so it opens no replication
/// listener and talks to no other node. It also does not report telemetry, and a node without a license
/// key issues itself a single-node license rather than asking for one, so nothing here reaches out over
/// the network on its own.
/// </para>
/// <para>
/// Use <see cref="CreateClientSettings"/> to point the KurrentDB .NET client at the socket, or
/// <see cref="CreateChannel"/> for a gRPC channel to use the generated service clients directly.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// await using var db = new EmbeddedKurrentDB(new EmbeddedKurrentDBOptions { DataDirectory = "./data" });
/// await db.StartAsync();
///
/// await using var client = new KurrentDBClient(db.CreateClientSettings());
/// await client.AppendToStreamAsync("orders-1", StreamState.Any, [ /* ... */ ]);
/// </code>
/// </example>
[PublicAPI]
public sealed class EmbeddedKurrentDB : IAsyncDisposable {
	static Serilog.ILogger Log => Serilog.Log.ForContext<EmbeddedKurrentDB>();

	readonly EmbeddedKurrentDBOptions _options;
	readonly IConfigurationRoot _configuration;
	readonly TimeSpan _startupTimeout;

	// One lock for every lifecycle transition, so starting, stopping and disposing never overlap and the
	// state below is only ever read and written while holding it. None of this is a hot path. It is held
	// across awaits, hence an async lock, and it is never disposed: a disposed one would throw for the
	// second DisposeAsync rather than returning.
	readonly AsyncExclusiveLock _lifecycle = new();

	State _state;

	// opened by StartAsync, and only read while holding _lifecycle or after a successful start
	ClusterVNodeHostedService? _hostedService;
	WebApplication? _web;
	ReadinessProbe? _readiness;

	// written from the Kestrel callback, read by anyone holding a reference
	string? _unixSocketPath;

	/// <summary>
	/// Works out how the node will be configured and prepares its directory. Nothing is opened and nothing
	/// is listening until <see cref="StartAsync"/> is called.
	/// </summary>
	public EmbeddedKurrentDB(EmbeddedKurrentDBOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);

		_startupTimeout = options.StartupTimeout > TimeSpan.Zero
			? options.StartupTimeout
			: throw new ArgumentOutOfRangeException(
				nameof(options), options.StartupTimeout,
				$"{nameof(EmbeddedKurrentDBOptions.StartupTimeout)} must be greater than zero.");

		// the directory is owner-only when we create it, which is also what keeps another user off the
		// socket inside it between the moment it is bound and the moment its own permissions are set
		DataDirectory = UnixDomainSocket.CreatePrivateDirectory(options.DataDirectory);

		_options = options;
		_configuration = BuildConfiguration(options, DataDirectory);
		ServerOptions = ClusterVNodeOptions.FromConfiguration(_configuration);

		ClusterVNodeOptionsValidator.Validate(ServerOptions);

		if (!ClusterVNodeOptionsValidator.ValidateForStartup(ServerOptions)) {
			throw new InvalidConfigurationException(
				"The embedded database cannot start with this configuration. The errors logged above say why.");
		}

		// a node with no database directory does not listen on a socket, which would leave this one with
		// no way in at all
		if (ServerOptions.Database.MemDb) {
			throw new InvalidOperationException(
				"An embedded KurrentDB cannot run with an in-memory database: it is reached over a UNIX domain " +
				"socket, and the node only creates one when it has a database directory.");
		}
	}

	/// <summary>The directory the database is written to.</summary>
	public string DataDirectory { get; }

	/// <summary>
	/// The UNIX domain socket clients connect over. The node reports it when it binds, so this is only
	/// available once <see cref="StartAsync"/> has returned.
	/// </summary>
	public string UnixSocketPath =>
		Volatile.Read(ref _unixSocketPath)
		?? throw new InvalidOperationException("The embedded database has not been started.");

	/// <summary>The server options the node was configured with.</summary>
	public ClusterVNodeOptions ServerOptions { get; }

	/// <summary>
	/// The node's service provider, for resolving services such as <c>ISystemClient</c>. Available once
	/// <see cref="StartAsync"/> has returned.
	/// </summary>
	public IServiceProvider Services =>
		_web?.Services
		?? throw new InvalidOperationException("The embedded database has not been started.");

	/// <summary>
	/// Starts the node and returns once it reports itself ready to serve requests.
	/// </summary>
	/// <exception cref="TimeoutException">
	/// The node did not become ready within <see cref="EmbeddedKurrentDBOptions.StartupTimeout"/>.
	/// </exception>
	public async Task StartAsync(CancellationToken cancellationToken = default) {
		await _lifecycle.AcquireAsync(cancellationToken);
		try {
			ObjectDisposedException.ThrowIf(_state is State.Disposed, this);

			if (_state is not State.Created) {
				throw new InvalidOperationException(
					"An embedded database runs once. The node and the host it runs in are both single-use, " +
					"so starting it again after it has run is not possible: construct another " +
					$"{nameof(EmbeddedKurrentDB)} on the same data directory instead.");
			}

			if (!NodePreflight.TryPrepare(ServerOptions, out var cannotStart))
				throw new InvalidOperationException(cannotStart);

			if (!CertificateProviders.TryCreate(ServerOptions, out var certificateProvider, out var noCertificate))
				throw new InvalidOperationException(noCertificate);

			_state = State.Running;

			try {
				// opening the database is synchronous and can take a while, and there is no reason to make
				// the caller's thread wait through it
				_hostedService = await Task.Run(
					() => new ClusterVNodeHostedService(ServerOptions, certificateProvider, _configuration),
					cancellationToken);

				_web = BuildWebApplication(_options, _configuration, _hostedService);

				// subscribe before anything starts, so the ready message cannot be missed
				_readiness = new ReadinessProbe(_web.Services.GetRequiredService<ISubscriber>());

				await _web.StartAsync(cancellationToken);

				// the socket is bound by now: a connection over it is authenticated as the system account,
				// so it is restricted to the user who started the database
				UnixDomainSocket.RestrictToOwner(UnixSocketPath);

				await _readiness.WaitAsync(_startupTimeout, cancellationToken);
			} catch {
				// a node that failed to start is not one anybody can use, and it is holding the database:
				// let go of whatever did open, which leaves this one disposed and refusing another attempt
				await TearDownAsync();
				throw;
			}
		} finally {
			_lifecycle.Release();
		}

		Log.Information("Embedded KurrentDB is ready on {unixSocket}", UnixSocketPath);
	}

	/// <summary>
	/// Stops the node. Does nothing if it was never started or has already been stopped, and calling it
	/// is optional: disposing stops the node too.
	/// </summary>
	public async Task StopAsync(CancellationToken cancellationToken = default) {
		await _lifecycle.AcquireAsync(cancellationToken);
		try {
			await StopWhileLockedAsync(cancellationToken);
		} finally {
			_lifecycle.Release();
		}
	}

	/// <summary>
	/// Stops the node, assuming <see cref="_lifecycle"/> is held. The node answers a second stop with a
	/// warning, so only the first one through here reaches it.
	/// </summary>
	async Task StopWhileLockedAsync(CancellationToken cancellationToken) {
		if (_state is not State.Running || _web is null)
			return;

		await _web.StopAsync(cancellationToken);
		_state = State.Stopped;
	}

	/// <summary>
	/// Settings for the KurrentDB .NET client that reach this node over its socket.
	/// </summary>
	public KurrentDBClientSettings CreateClientSettings() {
		var settings = KurrentDBClientSettings.Create("kurrentdb://localhost?tls=false");

		var socketPath = UnixSocketPath;
		settings.CreateHttpMessageHandler = () => UnixDomainSocket.CreateHandler(socketPath);

		return settings;
	}

	/// <summary>
	/// A gRPC channel to this node over its socket, for use with the generated service clients.
	/// </summary>
	/// <remarks>
	/// The caller owns the channel and should dispose it. As with <see cref="CreateClientSettings"/>, the
	/// address is a placeholder: the handler dials the socket.
	/// </remarks>
	public GrpcChannel CreateChannel(Action<GrpcChannelOptions>? configure = null) {
		var channelOptions = new GrpcChannelOptions {
			HttpHandler = UnixDomainSocket.CreateHandler(UnixSocketPath),
			DisposeHttpClient = true,
		};

		configure?.Invoke(channelOptions);

		return GrpcChannel.ForAddress("http://localhost", channelOptions);
	}

	/// <summary>
	/// Stops the node and releases the database. A second call waits for the first to finish and then
	/// returns, so it never hands back a database that is still being torn down.
	/// </summary>
	public async ValueTask DisposeAsync() {
		await _lifecycle.AcquireAsync(CancellationToken.None);
		try {
			if (_state is not State.Disposed)
				await TearDownAsync();
		} finally {
			_lifecycle.Release();
		}
	}

	/// <summary>
	/// Lets go of everything that was opened, in the reverse of the order it was opened in, and leaves the
	/// database disposed however that goes. Assumes <see cref="_lifecycle"/> is held, and copes with a
	/// start that only got part of the way.
	/// </summary>
	async Task TearDownAsync() {
		try {
			try {
				await StopWhileLockedAsync(CancellationToken.None);
			} catch (Exception ex) {
				Log.Warning(ex, "The embedded database did not stop cleanly.");
			}

			_readiness?.Dispose();

			if (_web is not null)
				await _web.DisposeAsync();

			// A node closes its own database as it shuts down, and closing twice is a no-op. This is for
			// the start that failed after the node was constructed: the constructor opens the checkpoints
			// and takes the exclusive lock on the directory, and nothing else would ever let go of them.
			// The server executable can leave that to process exit; a host that embeds the database and
			// carries on cannot.
			if (_hostedService is not null)
				await _hostedService.Node.Db.DisposeAsync();

			if (Volatile.Read(ref _unixSocketPath) is { } socketPath)
				UnixDomainSocket.Delete(socketPath);
		} finally {
			_state = State.Disposed;
		}
	}

	/// <summary>
	/// The states an embedded database moves through, in order and only forwards. It runs once: both the
	/// node and the host it runs in are single-use, so there is no way back to <see cref="Created"/>.
	/// </summary>
	enum State {
		Created,
		Running,
		Stopped,
		Disposed
	}

	/// <remarks>
	/// <para>
	/// This library's choices go in as default values, which are keyed without the <c>KurrentDB:</c>
	/// prefix because that source adds it. The caller's settings go in afterwards, so they win, and they
	/// carry the prefix as a configuration file would. Only the second of those is subject to the
	/// environment-only rule, which is what lets the startup checks run at all.
	/// </para>
	/// <para>
	/// Either way the engine's own settings are flat under <c>KurrentDB:</c>, because
	/// <see cref="ClusterVNodeOptions.FromConfiguration"/> binds each option group straight from that
	/// section — the groups are for the help text, not for the keys. Only plugins are nested, as in
	/// <c>KurrentDB:AutoScavenge:Enabled</c>, because they read their own sections. Getting this wrong is
	/// quiet: an engine key under a group name looks like an unknown plugin section, which the unknown
	/// option check deliberately ignores.
	/// </para>
	/// </remarks>
	static IConfigurationRoot BuildConfiguration(EmbeddedKurrentDBOptions options, string dataDirectory) =>
		new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			// TelemetryOptout value masquerades as a default because it
			// is only allowed to be overridden by the environment.
			.AddKurrentDefaultValues(new KeyValuePair<string, string?>[] {
				new("KurrentDB:TelemetryOptout", options.TelemetryOptout.ToString()),
			})
			.AddInMemoryCollection([
				new("KurrentDB:Db", dataDirectory),
				new("KurrentDB:EnableUnixSocket", bool.TrueString),
				new("KurrentDB:Insecure", bool.TrueString),
				//qq revisit stats and logging
				new("KurrentDB:DisableLogFile", bool.TrueString),
				new("KurrentDB:StatsStorage", nameof(StatsStorage.None)),
			])
			.AddInMemoryCollection(options.DatabaseOptions)
			.Build();

	WebApplication BuildWebApplication(
		EmbeddedKurrentDBOptions options,
		IConfigurationRoot configuration,
		ClusterVNodeHostedService hostedService) {

		var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
			// the application name is left to the host: the admin UI's static assets are published next to
			// the assemblies of whichever application is hosting the node, under that application's name
			ContentRootPath = AppContext.BaseDirectory
		});

		builder.Configuration.AddConfiguration(configuration);
		NodePreflight.WriteHostEnvironment(builder.Environment);

		// everything the node logs goes through the static Serilog logger. A host that has not configured
		// Serilog gets a quiet component rather than console output it never asked for.
		builder.Logging.ClearProviders().AddSerilog();
		options.ConfigureLogging?.Invoke(builder.Logging);

		builder.Services.Configure<HostOptions>(host => {
			host.ShutdownTimeout = ClusterVNode.ShutdownTimeout + TimeSpan.FromSeconds(1);
			host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
		});

		builder.WebHost.ConfigureKestrel(server => {
			// the node picks the socket path — the database directory, which is the data directory — and
			// reports it back. The constructor has ruled out the in-memory database, so what is left is an
			// operating system without UNIX domain sockets, or a caller who turned EnableUnixSocket off
			// through DatabaseOptions. Either way an embedded database has no way in, so neither is
			// survivable.
			if (!KestrelHelpers.TryConfigureListeners(
				server: server,
				options: ServerOptions,
				hostedService: hostedService,
				listenOnTcp: false,
				unixSocket: out var unixSocket)) {
				throw new PlatformNotSupportedException(
					"An embedded KurrentDB is reached over a UNIX domain socket, and none was opened. Either " +
					"this operating system does not support them, or KurrentDB:EnableUnixSocket has been " +
					"turned off through DatabaseOptions.");
			}

			Volatile.Write(ref _unixSocketPath, unixSocket);
		});

		NodeWebApplication.ConfigureServices(builder.Services, hostedService);

		// last, so that it covers the host's hosted services as well as the node's
		NodeWebApplication.LogHostedServiceLifecycle(builder.Services);

		var app = builder.Build();

		NodeWebApplication.Configure(app, hostedService);

		return app;
	}

	/// <summary>
	/// Completes when the node publishes <see cref="SystemMessage.SystemReady"/>.
	/// </summary>
	sealed class ReadinessProbe : IHandle<SystemMessage.SystemReady>, IDisposable {
		// the message is handled on the bus dispatch thread; continuations must not run there
		readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
		readonly ISubscriber _mainBus;

		public ReadinessProbe(ISubscriber mainBus) {
			_mainBus = mainBus;
			_mainBus.Subscribe(this);
		}

		void IHandle<SystemMessage.SystemReady>.Handle(SystemMessage.SystemReady message) {
			if (_ready.TrySetResult())
				_mainBus.Unsubscribe(this);
		}

		public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) {
			try {
				await _ready.Task.WaitAsync(timeout, cancellationToken);
			} catch (TimeoutException) {
				throw new TimeoutException(
					$"The embedded database was not ready within {timeout}. See the log for what it was doing.");
			}
		}

		public void Dispose() {
			if (_ready.TrySetCanceled())
				_mainBus.Unsubscribe(this);
		}
	}
}
