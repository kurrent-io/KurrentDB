// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Polly;
using Serilog;

namespace EventStore.Core.Resilience;

public static class ResilienceKeys {
	public static ResiliencePropertyKey<ILogger> Logger { get; } = new("logger");
}
