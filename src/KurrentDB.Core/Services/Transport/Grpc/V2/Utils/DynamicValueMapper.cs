// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Protobuf.Collections;
using KurrentDB.Protobuf;

namespace KurrentDB.Core.Services.Transport.Grpc.V2.Utils;

[PublicAPI]
public static class DynamicValueMapper {
	public static Dictionary<string, string?> MapToDictionary(this MapField<string, DynamicValue> source) {
		return source.Aggregate(
			new Dictionary<string, string?>(),
			(seed, entry) => {
				seed.Add(entry.Key, MapToValueString(entry.Value));
				return seed;
			});

		static string? MapToValueString(DynamicValue source) {
			return source.KindCase switch {
				DynamicValue.KindOneofCase.NullValue      => null,
				DynamicValue.KindOneofCase.None           => null,
				DynamicValue.KindOneofCase.StringValue    => source.StringValue,
				DynamicValue.KindOneofCase.BooleanValue   => source.BooleanValue.ToString(),
				DynamicValue.KindOneofCase.Int32Value     => source.Int32Value.ToString(),
				DynamicValue.KindOneofCase.Int64Value     => source.Int64Value.ToString(),
				DynamicValue.KindOneofCase.FloatValue     => source.FloatValue.ToString(CultureInfo.InvariantCulture),
				DynamicValue.KindOneofCase.DoubleValue    => source.DoubleValue.ToString(CultureInfo.InvariantCulture),
				DynamicValue.KindOneofCase.TimestampValue => source.TimestampValue.ToDateTimeOffset().ToString("O"), // always datetime offset?
				DynamicValue.KindOneofCase.DurationValue  => source.DurationValue.ToTimeSpan().ToString("c"),
				DynamicValue.KindOneofCase.BytesValue     => source.BytesValue.ToBase64(),
				_                                         => throw new NotSupportedException($"Unsupported value type: {source.KindCase}")
			};
		}
	}

	public static bool TryMapValue<T>(this DynamicValue source, out T? value) {
		try {
			object? mappedValue = source.KindCase switch {
				DynamicValue.KindOneofCase.NullValue      => null,
				DynamicValue.KindOneofCase.None           => null,
				DynamicValue.KindOneofCase.StringValue    => source.StringValue,
				DynamicValue.KindOneofCase.BooleanValue   => source.BooleanValue,
				DynamicValue.KindOneofCase.Int32Value     => source.Int32Value,
				DynamicValue.KindOneofCase.Int64Value     => source.Int64Value,
				DynamicValue.KindOneofCase.FloatValue     => source.FloatValue,
				DynamicValue.KindOneofCase.DoubleValue    => source.DoubleValue,
				DynamicValue.KindOneofCase.TimestampValue => source.TimestampValue.ToDateTimeOffset(),
				DynamicValue.KindOneofCase.DurationValue  => source.DurationValue.ToTimeSpan(),
				DynamicValue.KindOneofCase.BytesValue     => (ReadOnlyMemory<byte>)source.BytesValue.ToByteArray(),
				_                                         => throw new NotSupportedException($"Unsupported value type: {source.KindCase}")
			};

			if (mappedValue is T typedValue) {
				value = typedValue;
				return true;
			}

			// Handle null case for nullable reference types
			if (mappedValue is null && !typeof(T).IsValueType) {
				value = default;
				return true;
			}

			// Handle null case for nullable value types
			if (mappedValue is null && typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Nullable<>)) {
				value = default;
				return true;
			}

			value = default;
			return false;
		}
		catch {
			value = default;
			return false;
		}
	}

	public static bool TryGetValue<T>(this MapField<string, DynamicValue> source, string key, out T? value) {
		value = default;
		return source.TryGetValue(key, out var dynamicValue) && dynamicValue.TryMapValue(out value);
	}

	public static T? GetRequiredValue<T>(this MapField<string, DynamicValue> source, string key) =>
		source.TryGetValue(key, out var dynamicValue)
			? dynamicValue.TryMapValue<T>(out var value)
				? value
				: throw new InvalidCastException($"Cannot cast `{key}` to {typeof(T).Name}")
			: throw new KeyNotFoundException($"Required value '{key}' is missing in the source map.");
}
