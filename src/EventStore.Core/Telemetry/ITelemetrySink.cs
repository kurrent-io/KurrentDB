// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Telemetry;


public interface ITelemetrySink {
	Task Flush(JsonObject data, CancellationToken token);
}
