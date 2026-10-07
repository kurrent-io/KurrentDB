// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Embedded;

/// <summary>
/// How an <see cref="EmbeddedKurrentDB"/> is configured.
/// </summary>
/// <remarks>
/// These are the settings that an embedded database has an opinion about. Everything else the server
/// understands can be set through <see cref="Settings"/>, using the same keys as a configuration file —
/// <c>KurrentDB:Database:ChunkSize</c> and so on.
/// </remarks>
[PublicAPI]
public sealed record EmbeddedKurrentDBOptions {
	/// <summary>
	/// The directory the database is written to. It is created, owner-only, if it does not exist.
	/// </summary>
	public required string DataDirectory { get; init; }

	/// <summary>
	/// The directory the index is written to. Defaults to an <c>index</c> directory under
	/// <see cref="DataDirectory"/>.
	/// </summary>
	public string? IndexDirectory { get; init; }

	/// <summary>
	/// Whether to also listen on a TCP port. The socket is the intended way in; the port is what serves
	/// the admin UI, the Prometheus endpoint and any tooling that cannot speak to a socket.
	/// </summary>
	// TODO: revisit. The TCP listener is on by default so that the admin UI and the existing HTTP tooling
	// keep working while the embedded server finds its shape. An embedded database should not need to open
	// a port at all, and this should end up defaulting to false once nothing depends on it.
	public bool EnableTcpListener { get; init; } = true;

	/// <summary>
	/// The address the TCP listener binds to, when <see cref="EnableTcpListener"/> is set. Loopback by
	/// default: a single embedded node has nothing to say to another machine.
	/// </summary>
	public IPAddress TcpListenerIp { get; init; } = IPAddress.Loopback;

	/// <summary>
	/// The port the TCP listener binds to, when <see cref="EnableTcpListener"/> is set.
	/// </summary>
	public int TcpListenerPort { get; init; } = 2113;

	/// <summary>
	/// Whether to run the projections subsystem and start the standard projections.
	/// </summary>
	public bool RunProjections { get; init; } = true;

	/// <summary>
	/// Whether to run without TLS, authentication or authorization.
	/// </summary>
	/// <remarks>
	/// On by default, and it means what it says: anything that can reach the socket or the TCP port has
	/// administrator access. The socket is restricted to the user who started the database, but the TCP
	/// listener is open to everything on the machine that can reach the loopback address. Turn this off
	/// and configure certificates through <see cref="Settings"/> for a node that is reachable by others.
	/// </remarks>
	public bool Insecure { get; init; } = true;

	/// <summary>
	/// How long to wait for the node to report itself ready before <see cref="EmbeddedKurrentDB.StartAsync"/>
	/// gives up.
	/// </summary>
	public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Server settings, keyed the same way a configuration file is: <c>KurrentDB:Section:Option</c>.
	/// Applied last, so they win over everything above.
	/// </summary>
	public IReadOnlyDictionary<string, string?> Settings { get; init; } =
		new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Called after the node has registered its own services, to add or replace registrations.
	/// </summary>
	public Action<ClusterVNodeOptions, IServiceCollection>? ConfigureServices { get; init; }

	/// <summary>
	/// Called after logging has been pointed at Serilog, to change where the node logs.
	/// </summary>
	public Action<ILoggingBuilder>? ConfigureLogging { get; init; }
}
