// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;

namespace KurrentDB.Surge.Testing.Extensions;

public static class ConfigurationExtensions {
	public static IConfiguration EnsureValue(this IConfiguration configuration, string key, string defaultValue) {
		var value = configuration.GetValue<string?>(key);

		if (string.IsNullOrEmpty(value))
			configuration[key] = defaultValue;

		return configuration;
	}
}