// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using static System.Environment;

namespace EventStore.Core.Configuration.Sources;

public class EventStoreEnvironmentVariablesConfigurationProvider(IDictionary? environment = null) : ConfigurationProvider {
	IDictionary? Environment { get; } = environment;

	public override void Load() {
		var environment = Environment ?? GetEnvironmentVariables();

		var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

		foreach (var key in environment.Keys) {
			if (KurrentConfigurationKeys.TryNormalizeEventStoreEnvVar(key, out var normalizedKey))
				data[normalizedKey] = environment[key]?.ToString();
		}

		Data = data;
	}
}
public class EnvironmentVariablesSource(IDictionary? environment = null) : IConfigurationSource {
	public IConfigurationProvider Build(IConfigurationBuilder builder) =>
		new EventStoreEnvironmentVariablesConfigurationProvider(environment);
}

public static class EventStoreEnvironmentVariablesConfigurationExtensions {
	public static IConfigurationBuilder AddLegacyEventStoreEnvironmentVariables(this IConfigurationBuilder builder, IDictionary? environment = null) =>
		builder.Add(new EnvironmentVariablesSource(environment));

	public static IConfigurationBuilder AddLegacyEventStoreEnvironmentVariables(this IConfigurationBuilder builder, Dictionary<string, string> environment) =>
		builder.Add(new EnvironmentVariablesSource(environment));

	public static IConfigurationBuilder AddLegacyEventStoreEnvironmentVariables(this IConfigurationBuilder builder, params (string Key, string Value)[] environment) =>
		builder.Add(new EnvironmentVariablesSource(environment.ToDictionary(x => x.Key, x => x.Value)));
}
