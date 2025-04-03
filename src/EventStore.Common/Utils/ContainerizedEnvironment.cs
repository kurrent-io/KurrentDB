// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime;

namespace EventStore.Common.Utils;

public static class ContainerizedEnvironment {
    public const int ReaderThreadCount       = 4;
    public const int WorkerThreadCount       = 5;
    public const int StreamInfoCacheCapacity = 100000;

    public static bool IsRunningInContainer() => RuntimeInformation.IsRunningInContainer;
}
