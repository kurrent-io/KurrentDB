// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.POC.IO.Core;

/// <summary>
/// A client abstraction used for administrative operations.
/// </summary>
public interface IOperationsClient {
	/// <summary>
	/// Demotes the current node to a follower if it is a leader. Don't expect the node to be demoted immediately. The
	/// call start demotion process.
	/// </summary>
	void Resign();
}
