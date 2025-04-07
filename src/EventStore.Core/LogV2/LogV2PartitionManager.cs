// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.LogV2;

public class LogV2PartitionManager : IPartitionManager {

	public Guid? RootId => Guid.Empty;
	public Guid? RootTypeId => Guid.Empty;

	public void Initialize() { }
}
