// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using static System.String;
using static System.StringComparison;

namespace EventStore.Core.Configuration.Sources;

public static class EventStoreConfigurationKeys {
	public const string Prefix = "EventStore";

	private const string EnvVarKeyDelimiter = "__";
	private const string ArgWordDelimiter = "-";
	private const string EnvVarWordDelimiter = "_";

	private static readonly char[] Delimiters = { '-', '_', ':' };

	private static readonly IReadOnlyList<string> SectionKeys;
	private static readonly IReadOnlyList<string> OptionsKeys;
	private static readonly IReadOnlyList<string> AllKnownKeys;

	static EventStoreConfigurationKeys() {
		SectionKeys = typeof(ClusterVNodeOptions).GetProperties()
			.Where(prop => prop.GetCustomAttribute<ClusterVNodeOptions.OptionGroupAttribute>() != null)
			.Select(property => property.Name)
			.ToList();

		OptionsKeys = typeof(ClusterVNodeOptions).GetProperties()
			.Where(prop => prop.GetCustomAttribute<ClusterVNodeOptions.OptionGroupAttribute>() != null)
			.SelectMany(
				property => property.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			.Select(x => x.Name)
			.ToList();

		AllKnownKeys = SectionKeys.Concat(OptionsKeys).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
	}

	// outputs a key for IConfiguration e.g. EventStore:StreamInfoCacheCapacity
	public static string Normalize(string key) {
		// if the key doesn't contain any delimiters,
		// we can just get out, transforming the key
		// if needed because of cli lowercase args
		if (!key.Any(Delimiters.Contains))
			return $"{Prefix}:{Transform(key)}";

		// remove the prefix and normalize the key
		var keys = (key.StartsWith(Prefix, OrdinalIgnoreCase) ? key.Remove(0, Prefix.Length) : key)
			.Replace(EnvVarKeyDelimiter, ConfigurationPath.KeyDelimiter)
			.Replace(ArgWordDelimiter, EnvVarWordDelimiter)
			.Split(ConfigurationPath.KeyDelimiter)
			.Where(x => x.Length > 0)
			.Select(Transform);

		return $"{Prefix}:{Join(ConfigurationPath.KeyDelimiter, keys)}";

		// because we know all keys, we can ensure the name is always correct
		static string Transform(string key) {
			var value = Pascalize(key);
			return AllKnownKeys.FirstOrDefault(x => x.Equals(value, OrdinalIgnoreCase)) ?? value;
		}

		static string Pascalize(string key) =>
			Join(Empty, key
				.ToLowerInvariant()
				.Split(EnvVarWordDelimiter)
				.Select(word => new string(word.Select((c, i) => i == 0 ? char.ToUpper(c) : c).ToArray()))
			);
	}

	/// <summary>
	/// Determines if the given key is an event store environment variable.
	/// </summary>
	public static bool IsEventStoreEnvVar(string? key) =>
		key is not null && key.StartsWith($"{Prefix}{EnvVarWordDelimiter}", OrdinalIgnoreCase);

	/// <summary>
	/// Only normalizes the given key if it is an event store environment variable.
	/// </summary>
	public static bool TryNormalizeEnvVar(string key, [MaybeNullWhen(false)] out string normalizedKey) =>
		(normalizedKey = IsEventStoreEnvVar(key) ? Normalize(key) : null) is not null;

	/// <summary>
	/// Only normalizes the given key if it is an event store environment variable.
	/// </summary>
	public static bool TryNormalizeEnvVar(object? key, [MaybeNullWhen(false)] out string normalizedKey) =>
		TryNormalizeEnvVar(key?.ToString() ?? Empty, out normalizedKey);

	public static string StripConfigurationPrefix(string key) =>
		key.StartsWith($"{Prefix}:", OrdinalIgnoreCase) ? key.Remove(0, Prefix.Length + 1) : key;
}
