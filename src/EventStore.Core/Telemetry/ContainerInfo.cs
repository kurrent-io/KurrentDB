// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime;

namespace EventStore.Core.Telemetry;

public readonly record struct ContainerInfo(bool IsContainer, bool IsKubernetes) {
    public static ContainerInfo Collect() => new(
        RuntimeInformation.IsRunningInContainer,
        RuntimeInformation.IsRunningInKubernetes
    );
}
