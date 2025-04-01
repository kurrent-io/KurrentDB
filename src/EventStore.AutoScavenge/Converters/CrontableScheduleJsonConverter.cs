// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;
using NCrontab;

namespace EventStore.AutoScavenge.Converters;

public class CrontableScheduleJsonConverter : JsonConverter<CrontabSchedule> {
	public override CrontabSchedule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException("CRON expression can only be a string");

		try {
			return CrontabSchedule.Parse(reader.GetString());
		} catch (Exception ex) {
			throw new JsonException("Invalid CRON expression", ex);
		}
	}

	public override void Write(Utf8JsonWriter writer, CrontabSchedule value, JsonSerializerOptions options) {
		JsonSerializer.Serialize(writer, value.ToString(), options);
	}
}
