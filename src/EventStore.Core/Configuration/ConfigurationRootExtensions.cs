// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using EventStore.Common.Configuration;
using EventStore.Common.Exceptions;
using EventStore.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration;

public static class ConfigurationRootExtensions {
	public static string[] CheckProvidersForLegacyEventStoreConfiguration(this IConfigurationRoot? configurationRoot) {
		if (configurationRoot == null)
			return [];
		var errorMessages = new List<string>();
		foreach (var provider in configurationRoot.Providers) {
			var source = provider.GetType();
			if (source == typeof(EventStoreCommandLineConfigurationProvider) ||
			    source == typeof(EventStoreEnvironmentVariablesConfigurationProvider) ||
			    source == typeof(EventStoreJsonFileConfigurationProvider)) {
				errorMessages.AddRange(
					provider.GetChildKeys().Select(
						key =>
							$"\"{key}\" Provided by: {ClusterVNodeOptions.GetSourceDisplayName(KurrentConfigurationKeys.Prefix + ":" + key, provider)}. " +
							$"The \"{KurrentConfigurationKeys.LegacyEventStorePrefix}\" configuration root " +
							$"has been deprecated, use \"{KurrentConfigurationKeys.Prefix}\" instead."));
			}
		}

		return errorMessages.ToArray();
	}

	public static string? CheckProvidersForEnvironmentVariables(this IConfigurationRoot? configurationRoot, IEnumerable<Type> optionSections) {
		if (configurationRoot == null)
			return null;

		var environmentOptionsOnly = optionSections.SelectMany(section => section.GetProperties())
			.Where(option => option.GetCustomAttribute<EnvironmentOnlyAttribute>() != null)
			.Select(option => option)
			.ToArray();

		var errorBuilder = new StringBuilder();

		foreach (var provider in configurationRoot.Providers) {
			var source = provider.GetType();

			if (source == typeof(KurrentDefaultValuesConfigurationProvider) ||
				source == typeof(KurrentDBEnvironmentVariablesConfigurationProvider) ||
				source == typeof(EventStoreEnvironmentVariablesConfigurationProvider))
				continue;

			var errorDescriptions =
				from key in provider.GetChildKeys()
				from property in environmentOptionsOnly
				where string.Equals(property.Name, key, StringComparison.CurrentCultureIgnoreCase)
				select (key, property.GetCustomAttribute<EnvironmentOnlyAttribute>()?.Message);

			errorBuilder.Append(
				errorDescriptions
					.Aggregate(
						new StringBuilder(),
						(sb, pair) => sb.AppendLine($"\"{pair.key}\" Provided by: {ClusterVNodeOptions.GetSourceDisplayName(KurrentConfigurationKeys.Prefix + ":" + pair.key, provider)}. {pair.Message}")
					)
			);
		}

		return errorBuilder.Length != 0 ? errorBuilder.ToString() : null;
	}

	public static string[] CheckProvidersForEventStoreDefaultLocations(
		this IConfigurationRoot? configurationRoot, LocationOptionWithLegacyDefault[] optionsWithLegacyDefaults) {
		if (configurationRoot is null) {
			return [];
		}

		var legacyLocationsProvider = configurationRoot
			.Providers.OfType<EventStoreDefaultLocationsProvider>().FirstOrDefault();
		if (legacyLocationsProvider is null) {
			return [];
		}

		var warnings = new List<string>();
		foreach (var legacyOption in optionsWithLegacyDefaults) {
			if (!legacyLocationsProvider.TryGet(legacyOption.OptionKey, out var value)) continue;
			if (value is not null) {
				warnings.Add($"\"{legacyOption.OptionKey}\": The default location \"{legacyOption.KurrentPath}\" was not found, using the legacy default location \"{value}\".");
			}
		}

		return warnings.ToArray();
	}

	public static T BindOptions<T>(this IConfiguration configuration) where T : new() {
		try {
			return configuration.Get<T>() ?? new T();
		} catch (InvalidOperationException ex) {
			var messages = new string?[] { ex.Message, ex.InnerException?.Message }
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.Select(x => x?.TrimEnd('.'));

			throw new InvalidConfigurationException(string.Join(". ", messages) + ".");
		}
	}
}
