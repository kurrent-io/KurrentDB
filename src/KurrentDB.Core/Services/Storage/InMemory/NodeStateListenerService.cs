// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;

namespace KurrentDB.Core.Services.Storage.InMemory;

// threading: we expect to handle one StateChangeMessage at a time, but Reads can happen concurrently
// with those handlings and with other reads.
public class NodeStateListenerService : IHandle<SystemMessage.StateChangeMessage> {
	public readonly SingleEventInMemoryStream Stream;

	public const string EventType = "$NodeStateChanged";

	private readonly JsonSerializerOptions _options = new() {
		Converters = {
			new JsonStringEnumConverter(),
		},
	};

	public NodeStateListenerService(IPublisher publisher, InMemoryLog memLog) {
		Stream = new(publisher, memLog, SystemStreams.NodeStateStream);
	}

	public void Handle(SystemMessage.StateChangeMessage message) {
		var payload = new { message.State };
		var data = JsonSerializer.SerializeToUtf8Bytes(payload, _options);
		Stream.Write(EventType, data);
	}
}
