// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventStore.POC.IO.Core.Serialization;

public class EnumConverterWithDefault<T> : JsonConverter<T> where T : struct, Enum {
	public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		var s = reader.GetString();
		if (!Enum.TryParse<T>(s, true, out var result) || !Enum.IsDefined(result))
			return default;
		return result;
	}

	public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) {
		writer.WriteStringValue(value.ToString());
	}
}
