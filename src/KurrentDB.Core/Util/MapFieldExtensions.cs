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
				case DynamicValue.KindOneofCase.FloatValue when value.FloatValue is float.NaN:
				case DynamicValue.KindOneofCase.DoubleValue when value.DoubleValue is double.NaN:
					writer.WriteStringValue("NaN");
					break;
				case DynamicValue.KindOneofCase.FloatValue when value.FloatValue is float.PositiveInfinity:
				case DynamicValue.KindOneofCase.DoubleValue when value.DoubleValue is double.PositiveInfinity:
					writer.WriteStringValue("Infinity");
					break;
				case DynamicValue.KindOneofCase.FloatValue when value.FloatValue is float.NegativeInfinity:
				case DynamicValue.KindOneofCase.DoubleValue when value.DoubleValue is double.NegativeInfinity:
					writer.WriteStringValue("-Infinity");
					break;

				case DynamicValue.KindOneofCase.NullValue:
				case DynamicValue.KindOneofCase.None:
					writer.WriteNullValue();
					break;
				case DynamicValue.KindOneofCase.StringValue:
					writer.WriteStringValue(value.StringValue);
					break;
				case DynamicValue.KindOneofCase.BooleanValue:
					writer.WriteBooleanValue(value.BooleanValue);
					break;
				case DynamicValue.KindOneofCase.Int32Value:
					writer.WriteNumberValue(value.Int32Value);
					break;
				case DynamicValue.KindOneofCase.Int64Value:
					writer.WriteNumberValue(value.Int64Value);
					break;
				case DynamicValue.KindOneofCase.FloatValue:
					writer.WriteNumberValue(value.FloatValue);
					break;
				case DynamicValue.KindOneofCase.DoubleValue:
					writer.WriteNumberValue(value.DoubleValue);
					break;
				case DynamicValue.KindOneofCase.TimestampValue:
					// ISO 8601
					writer.WriteStringValue(value.TimestampValue.ToDateTime());
					break;
				case DynamicValue.KindOneofCase.DurationValue:
					// no built in support for TimeSpan, format it ourselves as constant (invariant)
					writer.WriteStringValue(value.DurationValue.ToTimeSpan().ToString("c"));
					break;
				case DynamicValue.KindOneofCase.BytesValue:
					writer.WriteBase64StringValue(value.BytesValue.Span);
					break;
				default:
					throw new NotSupportedException($"Unsupported value type: {value.KindCase}");
			}
		}
	}
}
