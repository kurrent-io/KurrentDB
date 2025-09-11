// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Diagnostics.CodeAnalysis;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;

namespace Kurrent.Protobuf;

[PublicAPI]
public static class StructExtensions {
	public static bool TryGetString(this MapField<string, Value> source, string key, [NotNullWhen(true)] out string? result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetString(out result);
		result = null;
		return false;
	}

	public static bool TryGetBoolean(this MapField<string, Value> source, string key, out bool result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetBoolean(out result);
		result = false;
		return false;
	}

	public static bool TryGetDouble(this MapField<string, Value> source, string key, out double result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetDouble(out result);
		result = 0;
		return false;
	}

	public static bool TryGetInt32(this MapField<string, Value> source, string key, out int result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetInt32(out result);
		result = 0;
		return false;
	}

	public static bool TryGetInt64(this MapField<string, Value> source, string key, out long result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetInt64(out result);
		result = 0;
		return false;
	}

	public static bool TryGetSingle(this MapField<string, Value> source, string key, out float result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetSingle(out result);
		result = 0;
		return false;
	}

	public static bool TryGetDecimal(this MapField<string, Value> source, string key, out decimal result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetDecimal(out result);
		result = 0;
		return false;
	}

	public static bool TryGetGuid(this MapField<string, Value> source, string key, out Guid result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetGuid(out result);
		result = Guid.Empty;
		return false;
	}

	public static bool TryGetDateTime(this MapField<string, Value> source, string key, out DateTime result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetDateTime(out result);
		result = default;
		return false;
	}

	public static bool TryGetDateTimeOffset(this MapField<string, Value> source, string key, out DateTimeOffset result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetDateTimeOffset(out result);
		result = default;
		return false;
	}

	public static bool TryGetTimeSpan(this MapField<string, Value> source, string key, out TimeSpan result) {
		if (source.TryGetValue(key, out var value)) return value.TryGetTimeSpan(out result);
		result = TimeSpan.Zero;
		return false;
	}

	public static bool TryGetEnum<T>(this MapField<string, Value> source, string key, out T result) where T : struct, Enum {
		if (source.TryGetValue(key, out var value)) return value.TryGetEnum(out result);
		result = default;
		return false;
	}

	public static string GetString(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetString() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static bool GetBoolean(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetBoolean() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static double GetDouble(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetDouble() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static int GetInt32(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetInt32() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static long GetInt64(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetInt64() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static float GetSingle(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetSingle() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static decimal GetDecimal(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetDecimal() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static Guid GetGuid(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetGuid() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static DateTime GetDateTime(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetDateTime() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static DateTimeOffset GetDateTimeOffset(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetDateTimeOffset() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static TimeSpan GetTimeSpan(this MapField<string, Value> source, string key) =>
		source.TryGetValue(key, out var value) ? value.GetTimeSpan() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static T GetEnum<T>(this MapField<string, Value> source, string key) where T : struct, Enum =>
		source.TryGetValue(key, out var value) ? value.GetEnum<T>() : throw new KeyNotFoundException($"Key '{key}' not found");

	public static bool TryGetString(this Struct source, string key, [NotNullWhen(true)] out string? result) => source.Fields.TryGetString(key, out result);

	public static bool TryGetBoolean(this Struct source, string key, out bool result) => source.Fields.TryGetBoolean(key, out result);

	public static bool TryGetDouble(this Struct source, string key, out double result) => source.Fields.TryGetDouble(key, out result);

	public static bool TryGetInt32(this Struct source, string key, out int result) => source.Fields.TryGetInt32(key, out result);

	public static bool TryGetInt64(this Struct source, string key, out long result) => source.Fields.TryGetInt64(key, out result);

	public static bool TryGetSingle(this Struct source, string key, out float result) => source.Fields.TryGetSingle(key, out result);

	public static bool TryGetDecimal(this Struct source, string key, out decimal result) => source.Fields.TryGetDecimal(key, out result);

	public static bool TryGetGuid(this Struct source, string key, out Guid result) => source.Fields.TryGetGuid(key, out result);

	public static bool TryGetDateTime(this Struct source, string key, out DateTime result) => source.Fields.TryGetDateTime(key, out result);

	public static bool TryGetDateTimeOffset(this Struct source, string key, out DateTimeOffset result) => source.Fields.TryGetDateTimeOffset(key, out result);

	public static bool TryGetTimeSpan(this Struct source, string key, out TimeSpan result) => source.Fields.TryGetTimeSpan(key, out result);

	public static bool TryGetEnum<T>(this Struct source, string key, out T result) where T : struct, Enum => source.Fields.TryGetEnum<T>(key, out result);

	public static string GetString(this Struct source, string key) => source.Fields.GetString(key);

	public static bool GetBoolean(this Struct source, string key) => source.Fields.GetBoolean(key);

	public static double GetDouble(this Struct source, string key) => source.Fields.GetDouble(key);

	public static int GetInt32(this Struct source, string key) => source.Fields.GetInt32(key);

	public static long GetInt64(this Struct source, string key) => source.Fields.GetInt64(key);

	public static float GetSingle(this Struct source, string key) => source.Fields.GetSingle(key);

	public static decimal GetDecimal(this Struct source, string key) => source.Fields.GetDecimal(key);

	public static Guid GetGuid(this Struct source, string key) => source.Fields.GetGuid(key);

	public static DateTime GetDateTime(this Struct source, string key) => source.Fields.GetDateTime(key);

	public static DateTimeOffset GetDateTimeOffset(this Struct source, string key) => source.Fields.GetDateTimeOffset(key);

	public static TimeSpan GetTimeSpan(this Struct source, string key) => source.Fields.GetTimeSpan(key);

	public static T GetEnum<T>(this Struct source, string key) where T : struct, Enum => source.Fields.GetEnum<T>(key);
}
