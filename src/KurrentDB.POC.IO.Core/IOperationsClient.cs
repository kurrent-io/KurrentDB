// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.POC.IO.Core;

/// <summary>
/// A client abstraction used for administrative operations.
/// </summary>
public interface IOperationsClient {
	/// <summary>
	/// Demotes the current node to a follower if it is a leader. Don't expect the node to be demoted immediately. The
	/// call start demotion process.
	/// </summary>
	void Resign();

	/// <summary>
	/// Starts a scavenge on this node.
	/// </summary>
	/// <returns>The id of the started scavenge, or null if the node did not start one (e.g. one is already running)</returns>
	Task<Guid?> StartScavengeAsync(CancellationToken token);

	/// <summary>
	/// Gets the last scavenge this node ran since it started.
	/// </summary>
	Task<LastScavenge> GetLastScavengeAsync(CancellationToken token);

	/// <summary>
	/// Stops the scavenge with the given id on this node, or the current scavenge if no id is given.
	/// </summary>
	/// <returns>True if the scavenge was stopped, false if there was no such scavenge running</returns>
	Task<bool> StopScavengeAsync(Guid? scavengeId, CancellationToken token);
}

public record LastScavenge(Guid? ScavengeId, LastScavengeResult Result);

// Names match the result of the node's last scavenge
public enum LastScavengeResult {
	Unknown,
	InProgress,
	Success,
	Stopped,
	Errored,
}
