// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Hosting;

namespace KurrentDB.Embedded;

/// <summary>
/// The lifetime of the host an embedded node runs in. It listens for nothing: the process and its signals belong
/// to the embedding application, which stops the node through <see cref="EmbeddedKurrentDB.StopAsync"/> or
/// <see cref="EmbeddedKurrentDB.DisposeAsync"/> once it no longer needs it.
/// </summary>
sealed class EmbeddedHostLifetime : IHostLifetime {
	public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
