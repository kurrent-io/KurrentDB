// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf.Collections;
using KurrentDB.Protobuf;

namespace KurrentDB.Core.Util;

public static class MapFieldExtensions {
	static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new(JsonSerializerOptions.Default) {
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		Converters = {
			new DynamicValueConverter(),
		},
	};

	public static ReadOnlyMemory<byte> ToSyntheticMetadata(this MapField<string, DynamicValue> value) =>
		JsonSerializer.SerializeToUtf8Bytes(value, DefaultJsonSerializerOptions);

	class DynamicValueConverter : JsonConverter<DynamicValue> {
		public override DynamicValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			throw new NotImplementedException();
		}

		public override void Write(Utf8JsonWriter writer, DynamicValue value, JsonSerializerOptions options) {
			switch (value.KindCase) {
				case DynamicValue.KindOneofCase.NullValue:
				case DynamicValue.KindOneofCase.None:           writer.WriteNullValue();                                                      break;
				case DynamicValue.KindOneofCase.StringValue:    JsonSerializer.Serialize(writer, value.StringValue, options);                 break;
				case DynamicValue.KindOneofCase.BooleanValue:   JsonSerializer.Serialize(writer, value.BooleanValue, options);                break;
				case DynamicValue.KindOneofCase.Int32Value:     JsonSerializer.Serialize(writer, value.Int32Value, options);                  break;
				case DynamicValue.KindOneofCase.Int64Value:     JsonSerializer.Serialize(writer, value.Int64Value, options);                  break;
				case DynamicValue.KindOneofCase.FloatValue:     JsonSerializer.Serialize(writer, value.FloatValue, options);                  break;
				case DynamicValue.KindOneofCase.DoubleValue:    JsonSerializer.Serialize(writer, value.DoubleValue, options);                 break;
				case DynamicValue.KindOneofCase.TimestampValue: JsonSerializer.Serialize(writer, value.TimestampValue.ToDateTime(), options); break;
				case DynamicValue.KindOneofCase.DurationValue:  JsonSerializer.Serialize(writer, value.DurationValue.ToTimeSpan(), options);  break;
				case DynamicValue.KindOneofCase.BytesValue:     JsonSerializer.Serialize(writer, value.BytesValue.Memory, options);           break;
				default: throw new NotSupportedException($"Unsupported value type: {value.KindCase}");
			}
		}
	}
}
