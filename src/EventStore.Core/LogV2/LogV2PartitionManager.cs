// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV2;

public class LogV2PartitionManager : IPartitionManager {

	public Guid? RootId => Guid.Empty;
	public Guid? RootTypeId => Guid.Empty;

	public ValueTask Initialize(CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;
}
