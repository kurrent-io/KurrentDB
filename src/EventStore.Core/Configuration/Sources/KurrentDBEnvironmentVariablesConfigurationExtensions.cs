// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration.Sources;

public static class KurrentDBEnvironmentVariablesConfigurationExtensions {
	public static IConfigurationBuilder AddKurrentEnvironmentVariables(this IConfigurationBuilder builder, IDictionary? environment = null) =>
		builder.Add(new KurrentDBEnvironmentVariablesSource(environment));

	public static IConfigurationBuilder AddKurrentEnvironmentVariables(this IConfigurationBuilder builder, Dictionary<string, string> environment) =>
		builder.Add(new KurrentDBEnvironmentVariablesSource(environment));

	public static IConfigurationBuilder AddKurrentEnvironmentVariables(this IConfigurationBuilder builder, params (string Key, string Value)[] environment) =>
		builder.Add(new KurrentDBEnvironmentVariablesSource(environment.ToDictionary(x => x.Key, x => x.Value)));
}
