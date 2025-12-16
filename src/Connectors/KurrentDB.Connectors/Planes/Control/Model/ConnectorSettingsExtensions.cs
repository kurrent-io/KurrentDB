// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Collections;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Connectors.Control;

public static class ConnectorSettingsExtensions {
	public static IConfiguration MapToConnectorSettings(this MapField<string, string> source) {
		var dictionary = source.ToDictionary(kvp => kvp.Key, string? (kvp) => kvp.Value);
		return new ConfigurationBuilder().AddInMemoryCollection(dictionary).Build();
	}
}
