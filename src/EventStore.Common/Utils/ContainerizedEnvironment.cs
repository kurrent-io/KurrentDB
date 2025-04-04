// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Runtime;

namespace EventStore.Common.Utils;

public static class ContainerizedEnvironment {
	public const int ReaderThreadCount = 4;
	public const int WorkerThreadCount = 5;
	public const int StreamInfoCacheCapacity = 100000;

	public static bool IsRunningInContainer() => RuntimeInformation.IsRunningInContainer;
}
