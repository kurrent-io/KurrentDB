// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using Google.Protobuf.Collections;
using KurrentDB.Protobuf;

namespace KurrentDB.Core.Services.Transport.Grpc.V2.Utils;

[PublicAPI]
public static class DynamicValueMapper {
	public static bool TryGetValue<T>(this MapField<string, DynamicValue> source, string key, out T? value) {
		value = default;
		return source.TryGetValue(key, out var dynamicValue) && dynamicValue.TryMapValue(out value);
	}

	public static T? GetRequiredValue<T>(this MapField<string, DynamicValue> source, string key) =>
		source.TryGetValue(key, out var dynamicValue)
			? dynamicValue.TryMapValue<T>(out var value) ? value : throw new InvalidCastException($"Cannot cast `{key}` to {typeof(T).Name}")
			: throw new KeyNotFoundException($"Required value '{key}' is missing in the source map.");
}
