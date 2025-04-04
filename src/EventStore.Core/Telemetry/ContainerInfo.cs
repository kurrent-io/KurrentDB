// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Runtime;

namespace EventStore.Core.Telemetry;

public readonly record struct ContainerInfo(bool IsContainer, bool IsKubernetes) {
	public static ContainerInfo Collect() => new(
		RuntimeInformation.IsRunningInContainer,
		RuntimeInformation.IsRunningInKubernetes
	);
}
