// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.ClientAPI;
using EventStore.Common.Utils;

namespace EventStore.Core.Tests.ClientAPI.Helpers;

public class TestEvent {
	public static EventData NewTestEvent(string data = null, string metadata = null, string eventName = "TestEvent") {
		return NewTestEvent(Guid.NewGuid(), data, metadata, eventName);
	}

	public static EventData NewTestEvent(Guid eventId, string data = null, string metadata = null, string eventName = "TestEvent") {
		var encodedData = Helper.UTF8NoBom.GetBytes(data ?? eventId.ToString());
		var encodedMetadata = Helper.UTF8NoBom.GetBytes(metadata ?? "metadata");

		return new EventData(eventId, eventName, false, encodedData, encodedMetadata);
	}
}
