// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Embedded;

/// <summary>
/// How an <see cref="EmbeddedKurrentDB"/> is configured.
/// </summary>
[PublicAPI]
public sealed record EmbeddedKurrentDBOptions {
	/// <summary>
	/// The directory the database is written to. It is created, owner-only, if it does not exist.
	/// </summary>
	public required string DataDirectory { get; init; }

	/// <summary>
	/// Whether to opt out of telemetry. False by default.
	/// </summary>
	public bool TelemetryOptout { get; init; }

	/// <summary>
	/// How long to wait for the node to report itself ready before <see cref="EmbeddedKurrentDB.StartAsync"/>
	/// gives up.
	/// </summary>
	public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(1);

	/// <summary>
	/// Server settings, keyed as the server binds them e.g. <c>KurrentDB:PrepareTimeoutMs</c>
	/// </summary>
	public IReadOnlyDictionary<string, string?> DatabaseOptions { get; init; } =
		new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Called after logging has been pointed at Serilog, to change where the node logs.
	/// </summary>
	public Action<ILoggingBuilder>? ConfigureLogging { get; init; }
}
