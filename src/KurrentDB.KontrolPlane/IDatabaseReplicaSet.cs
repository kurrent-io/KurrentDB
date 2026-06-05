// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;

namespace KurrentDB.KontrolPlane;

public interface IDatabaseReplicaSet {
	ValueTask<ReplicaState> GetReplicaStateAsync(EndPoint address, CancellationToken token);
}
