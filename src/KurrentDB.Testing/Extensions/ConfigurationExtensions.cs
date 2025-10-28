// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace KurrentDB.Testing;

public static class ConfigurationExtensions {
	static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static T Bind<T>(this global::TUnit.Core.Interfaces.IConfiguration config, string key) where T : new() {
		var value = config.Get(key);
		return value is not { } json || JsonSerializer.Deserialize<T>(json, JsonOptions) is not { } result
			? new()
			: result;
	}
}
