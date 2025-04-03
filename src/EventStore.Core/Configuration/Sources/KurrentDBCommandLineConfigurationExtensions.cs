// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration.Sources;

public static class KurrentDBCommandLineConfigurationExtensions {
	public static IConfigurationBuilder AddKurrentCommandLine(this IConfigurationBuilder builder, params string[] args) =>
		builder.Add(new KurrentDBCommandLineConfigurationSource(args));
}
