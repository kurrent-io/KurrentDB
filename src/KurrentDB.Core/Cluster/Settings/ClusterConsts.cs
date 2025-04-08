// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Cluster.Settings;

public static class ClusterConsts {
	public const int SubscriptionLastEpochCount = 20;
	public static readonly TimeSpan TruncationSyncTimeout = TimeSpan.FromSeconds(60);
}
