// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using static System.StringComparer;

namespace EventStore.Core.Configuration.Sources;

public class KurrentDBDefaultValuesConfigurationSource(IEnumerable<KeyValuePair<string, string?>>? initialData = null) : IConfigurationSource {
	private IEnumerable<KeyValuePair<string, string?>> InitialData { get; } =
		initialData ?? new Dictionary<string, string?>();

	public IConfigurationProvider Build(IConfigurationBuilder builder) =>
		new KurrentDefaultValuesConfigurationProvider(InitialData);
}

public class KurrentDefaultValuesConfigurationProvider(IEnumerable<KeyValuePair<string, string?>> initialData)
	: MemoryConfigurationProvider(new(){
		InitialData = initialData.ToDictionary(
			kvp => $"{KurrentConfigurationKeys.Prefix}:{kvp.Key}",
			kvp => kvp.Value,
			OrdinalIgnoreCase)
	}) {
}

public static class EventStoreDefaultValuesConfigurationExtensions {
	public static IConfigurationBuilder AddKurrentDefaultValues(this IConfigurationBuilder configurationBuilder,
		IEnumerable<KeyValuePair<string, string?>> initialData) =>
		configurationBuilder.Add(new KurrentDBDefaultValuesConfigurationSource(initialData));

	public static IConfigurationBuilder AddKurrentDefaultValues(this IConfigurationBuilder builder,
		IEnumerable<KeyValuePair<string, object?>>? initialData) =>
		initialData is not null
			? builder.AddKurrentDefaultValues(initialData
				.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())).ToArray())
			: builder;

	public static IConfigurationBuilder AddKurrentDefaultValues(this IConfigurationBuilder builder) =>
		builder.AddKurrentDefaultValues(ClusterVNodeOptions.DefaultValues);
}
