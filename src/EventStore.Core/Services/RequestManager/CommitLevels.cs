// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.RequestManager;

public enum CommitLevel {
	Replicated, //Write on Cluster Quorum
	Indexed //Indexed on Leader
}
