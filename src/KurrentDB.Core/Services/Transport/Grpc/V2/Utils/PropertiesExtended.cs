// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using static System.Text.Json.JsonSerializer;
using static KurrentDB.Protobuf.DynamicValue.KindOneofCase;

namespace KurrentDB.Protobuf.Server;

public sealed partial class Properties {
	static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new(JsonSerializerOptions.Default) {
		NumberHandling         = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		Converters             = { new DynamicValueJsonConverter() }
	};

	public ReadOnlyMemory<byte> SerializeToBytes() =>
		SerializeToUtf8Bytes<IDictionary<string, DynamicValue>>(PropertiesValues, DefaultJsonSerializerOptions);

	class DynamicValueJsonConverter : JsonConverter<DynamicValue> {
		public override DynamicValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			throw new NotImplementedException();

		public override void Write(Utf8JsonWriter writer, DynamicValue value, JsonSerializerOptions options) {
			switch (value.KindCase) {
				case StringValue:    Serialize(writer, value.StringValue, options); break;
				case BooleanValue:   Serialize(writer, value.BooleanValue, options); break;
				case Int64Value:     Serialize(writer, value.Int64Value, options); break;
				case DoubleValue:    Serialize(writer, value.DoubleValue, options); break;
				case TimestampValue: Serialize(writer, value.TimestampValue.ToDateTime(), options); break;
				case DurationValue:  Serialize(writer, value.DurationValue.ToTimeSpan(), options); break;
				case BytesValue:     Serialize(writer, value.BytesValue.Memory, options); break;

				case NullValue or None: writer.WriteNullValue(); break;
			}
		}
	}
}
