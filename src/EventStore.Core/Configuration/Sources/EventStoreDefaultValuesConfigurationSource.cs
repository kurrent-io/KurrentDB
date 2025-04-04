// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

#nullable enable
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using static System.StringComparer;

namespace EventStore.Core.Configuration.Sources;

public class EventStoreDefaultValuesConfigurationSource(IEnumerable<KeyValuePair<string, string?>>? initialData = null) : IConfigurationSource {
	private IEnumerable<KeyValuePair<string, string?>> InitialData { get; } =
		initialData ?? new Dictionary<string, string?>();

	public IConfigurationProvider Build(IConfigurationBuilder builder) =>
		new EventStoreDefaultValuesConfigurationProvider(InitialData);
}

public class EventStoreDefaultValuesConfigurationProvider(IEnumerable<KeyValuePair<string, string?>> initialData)
	: MemoryConfigurationProvider(new() {
		InitialData = initialData.ToDictionary(
			kvp => $"{Prefix}:{kvp.Key}",
			kvp => kvp.Value,
			OrdinalIgnoreCase)
	}) {

	private const string Prefix = "EventStore";
}

public static class EventStoreDefaultValuesConfigurationExtensions {
	public static IConfigurationBuilder AddEventStoreDefaultValues(this IConfigurationBuilder configurationBuilder,
		IEnumerable<KeyValuePair<string, string?>> initialData) =>
		configurationBuilder.Add(new EventStoreDefaultValuesConfigurationSource(initialData));

	public static IConfigurationBuilder AddEventStoreDefaultValues(this IConfigurationBuilder builder,
		IEnumerable<KeyValuePair<string, object?>>? initialData) =>
		initialData is not null
			? builder.AddEventStoreDefaultValues(initialData
				.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value?.ToString())).ToArray())
			: builder;

	public static IConfigurationBuilder AddEventStoreDefaultValues(this IConfigurationBuilder builder) =>
		builder.AddEventStoreDefaultValues(ClusterVNodeOptions.DefaultValues);
}
