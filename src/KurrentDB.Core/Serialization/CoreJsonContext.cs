// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.Core.Serialization;

// In-memory system stream payloads use default (PascalCase) property names and string enums
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(GossipListenerService.GossipUpdatedPayload))]
[JsonSerializable(typeof(NodeStateListenerService.NodeStateChangedPayload))]
// Telemetry
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CoreJsonContext : JsonSerializerContext {
	public static JsonValue ToJsonValue(IConvertible value) => value?.GetTypeCode() switch {
		null or TypeCode.Empty or TypeCode.DBNull => null,
		TypeCode.Boolean => JsonValue.Create(value.ToBoolean(CultureInfo.InvariantCulture)),
		TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => JsonValue.Create(value.ToInt64(CultureInfo.InvariantCulture)),
		TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => JsonValue.Create(value.ToUInt64(CultureInfo.InvariantCulture)),
		TypeCode.Single => JsonValue.Create(value.ToSingle(CultureInfo.InvariantCulture)),
		TypeCode.Double => JsonValue.Create(value.ToDouble(CultureInfo.InvariantCulture)),
		TypeCode.Decimal => JsonValue.Create(value.ToDecimal(CultureInfo.InvariantCulture)),
		TypeCode.DateTime => JsonValue.Create(value.ToDateTime(CultureInfo.InvariantCulture)),
		_ => JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)), // Char, String, custom IConvertible
	};
}
