// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json.Nodes;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;

namespace EventStore.Core.Telemetry;

[DerivedMessage]
public abstract partial class TelemetryMessage : Message {
	[DerivedMessage(CoreMessage.Telemetry)]
	public partial class Request : TelemetryMessage {
		public readonly IEnvelope<Response> Envelope;

		public Request(IEnvelope<Response> envelope) {
			Ensure.NotNull(envelope, "envelope");

			Envelope = envelope;
		}
	}

	[DerivedMessage(CoreMessage.Telemetry)]
	public partial class Response : TelemetryMessage {
		public readonly string Root;
		public readonly string Key;
		public readonly JsonNode Value;

		public Response(string root, string key, JsonNode value) {
			Ensure.NotNull(root, "root");
			Ensure.NotNullOrEmpty(key, "key");
			Ensure.NotNull(value, "value");

			Root = root;
			Key = key;
			Value = value;
		}

		public Response(string key, JsonNode value) : this("", key, value) {
		}
	}

	[DerivedMessage(CoreMessage.Telemetry)]
	public partial class Collect : TelemetryMessage { }

	[DerivedMessage(CoreMessage.Telemetry)]
	public partial class Flush : TelemetryMessage { }
}
