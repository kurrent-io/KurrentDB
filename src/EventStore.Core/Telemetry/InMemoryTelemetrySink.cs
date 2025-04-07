// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.Telemetry;


public class InMemoryTelemetrySink : ITelemetrySink {
	public JsonObject Data { get; private set; }

	public Task Flush(JsonObject data, CancellationToken token) {
		Data = (JsonObject)JsonNode.Parse(data.ToJsonString());
		return Task.CompletedTask;
	}
}
