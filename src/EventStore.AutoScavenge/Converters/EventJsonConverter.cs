// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventStore.AutoScavenge.Converters;

public class EventJsonConverter : JsonConverter<IEvent> {
	public override IEvent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		throw new NotImplementedException();
	}

	public override void Write(Utf8JsonWriter writer, IEvent value, JsonSerializerOptions options) {
		JsonSerializer.Serialize(writer, value, value.GetType(), options);
	}
}
