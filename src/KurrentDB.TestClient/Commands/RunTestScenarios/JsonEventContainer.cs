// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.ClientAPI;
using EventStore.Common.Utils;
using EventStore.Transport.Http.Codecs;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal static class JsonEventContainer {
	public static EventData ForEvent(object @event) {
		if (@event == null)
			throw new ArgumentNullException("event");

		var encodedData = Helper.UTF8NoBom.GetBytes(Codec.Json.To(@event));
		var encodedMetadata =
			Helper.UTF8NoBom.GetBytes(Codec.Json.To(new Dictionary<string, object> {{"IsEmpty", true}}));

		return new EventData(Guid.NewGuid(), @event.GetType().Name, true, encodedData, encodedMetadata);
	}
}
