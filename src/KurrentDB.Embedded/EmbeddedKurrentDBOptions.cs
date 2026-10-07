// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
/// <c>KurrentDB:ChunkSize</c> and so on.
/// </remarks>
[PublicAPI]
public sealed record EmbeddedKurrentDBOptions {
	/// <summary>
	/// The directory the database is written to. It is created, owner-only, if it does not exist.
	/// </summary>
	public required string DataDirectory { get; init; }

	/// <summary>
	/// How long to wait for the node to report itself ready before <see cref="EmbeddedKurrentDB.StartAsync"/>
	/// gives up.
	/// </summary>
	public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Server settings, keyed as the server binds them: <c>KurrentDB:Option</c>, flat, with no section in
	/// between — <c>KurrentDB:ChunkSize</c>, not <c>KurrentDB:Database:ChunkSize</c>. Plugins are the
	/// exception and are nested, as in <c>KurrentDB:Licensing:LicenseKey</c>. Applied last, so they win
	/// over everything this library chose.
	/// </summary>
	/// <remarks>
	/// A key the server does not recognise stops the node from starting, so a typo is reported rather than
	/// ignored. That only covers flat keys: an unrecognised <em>section</em> is passed over without
	/// complaint, because that is how plugin configuration reaches its plugin. So
	/// <c>KurrentDB:Database:ChunkSize</c> is not an error — it simply does nothing.
	/// </remarks>
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
