// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace KurrentDB.AutoScavenge.Converters;

public class EventJsonConverter : JsonConverter<IEvent> {
	public override IEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		throw new NotImplementedException();
	}

	public override void Write(Utf8JsonWriter writer, IEvent value, JsonSerializerOptions options) {
		// options.TypeInfoResolver is always the source-generated AutoScavengeJsonContext, which knows every
		// concrete IEvent implementation, so this stays trim-safe despite serializing by the runtime type.
		JsonSerializer.Serialize(writer, value, options.GetTypeInfo(value.GetType()));
	}
}
