// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using EventStore.Common.Configuration;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using Microsoft.Extensions.Configuration;

#nullable enable
namespace EventStore.Core;

public partial record ClusterVNodeOptions {
	private static readonly IEnumerable<Type> OptionSections;
	public static readonly string HelpText;
	public string GetComponentName() => $"{Interface.NodeIp}-{Interface.NodePort}-cluster-node";
	public static readonly List<SectionMetadata> Metadata;

	static ClusterVNodeOptions() {
		OptionSections = typeof(ClusterVNodeOptions)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.GetCustomAttribute<OptionGroupAttribute>() != null)
			.Select(p => p.PropertyType);

		HelpText = GetHelpText();

		Metadata = typeof(ClusterVNodeOptions)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(prop => prop.GetCustomAttribute<OptionGroupAttribute>() != null)
			.Select(SectionMetadata.FromPropertyInfo)
			.ToList();

		DefaultValues = OptionSections.SelectMany(GetDefaultValues);

		return;

		static IEnumerable<KeyValuePair<string, object?>> GetDefaultValues(Type type) {
			var defaultInstance = Activator.CreateInstance(type)!;

			return type.GetProperties().Select(property =>
				new KeyValuePair<string, object?>(property.Name, property.PropertyType switch {
					{IsArray: true} => string.Join(",",
						((Array)(property.GetValue(defaultInstance) ?? Array.Empty<object>())).OfType<object>()),
					_ => property.GetValue(defaultInstance)
				}));
		}
	}

	public static IEnumerable<KeyValuePair<string, object?>> DefaultValues { get; }

	public string? DumpOptions() =>
		ConfigurationRoot == null ? null : ClusterVNodeOptionsPrinter.Print(LoadedOptions);

	public IReadOnlyDictionary<string, LoadedOption> LoadedOptions { get; init; } =
		new Dictionary<string, LoadedOption>();

	public string? GetDeprecationWarnings() {
		var defaultValues = new Dictionary<string, object?>(DefaultValues, StringComparer.OrdinalIgnoreCase);

		var deprecationWarnings = from section in OptionSections
			from option in section.GetProperties()
			let deprecationWarning = option.GetCustomAttribute<DeprecatedAttribute>()?.Message
			where deprecationWarning is not null
			let value = ConfigurationRoot?.GetValue<string?>(KurrentConfigurationKeys.Normalize(option.Name))
			where defaultValues.TryGetValue(option.Name, out var defaultValue)
			      && !string.Equals(value, defaultValue?.ToString(), StringComparison.OrdinalIgnoreCase)
			      select deprecationWarning;

		var builder = deprecationWarnings
			.Aggregate(new StringBuilder(), (builder, deprecationWarning) => builder.AppendLine(deprecationWarning));

		return builder.Length != 0 ? builder.ToString() : null;
	}

	public string? CheckForEnvironmentOnlyOptions() =>
		ConfigurationRoot.CheckProvidersForEnvironmentVariables(OptionSections);

	public string[] CheckForLegacyEventStoreConfiguration() =>
		ConfigurationRoot.CheckProvidersForLegacyEventStoreConfiguration();

	public string[] CheckForLegacyDefaultLocations(LocationOptionWithLegacyDefault[] optionsWithLegacyDefaults) =>
		ConfigurationRoot.CheckProvidersForEventStoreDefaultLocations(optionsWithLegacyDefaults);

	public static IReadOnlyDictionary<string, LoadedOption> GetLoadedOptions(IConfigurationRoot configurationRoot) {
		var loadedOptions = new Dictionary<string, LoadedOption>();

		// because we always start with defaults, we can just add them all first.
		// then we can override them with the actual values.
		foreach (var provider in configurationRoot.Providers) {
			foreach (var option in Metadata.SelectMany(x => x.Options)) {
				if (!provider.TryGet(option.Value.Key, out var value)) continue;

				var title = GetTitle(option);
				var sourceDisplayName = GetSourceDisplayName(option.Value.Key, provider);
				var isDefault = provider.GetType() == typeof(KurrentDefaultValuesConfigurationProvider);

				// Handle options that have been configured as arrays (GossipSeed is currently the only one
				// where this is possible)
				if (sourceDisplayName is "<UNKNOWN>" && value is null) {
					var parentPath = option.Value.Key;
					var childValues = new List<string>();

					foreach (var childKey in provider.GetChildKeys([], parentPath)) {
						var absoluteChildKey = parentPath + ":" + childKey;
						if (provider.TryGet(absoluteChildKey, out var childValue) && childValue is not null) {
							childValues.Add(childValue);
							sourceDisplayName = GetSourceDisplayName(absoluteChildKey, provider);
						}
					}

					value = string.Join(", ", childValues);
				}

				loadedOptions[option.Value.Key] = new(
					metadata: option.Value,
					title: title,
					value: value,
					sourceDisplayName: sourceDisplayName,
					isDefault: isDefault
				);
			}
		}

		return loadedOptions;

		static string GetTitle(KeyValuePair<string, OptionMetadata> option) =>
			CombineByPascalCase(KurrentConfigurationKeys.StripConfigurationPrefix(option.Value.Key)).ToUpper();
	}

	public static string GetSourceDisplayName(string key, IConfigurationProvider provider) {
		if (provider is KurrentDefaultValuesConfigurationProvider) {
			return "<DEFAULT>";
		} else if (provider is SectionProvider sectionProvider) {
			return sectionProvider.TryGetProviderFor(key, out var innerProvider)
				? GetSourceDisplayName(key, innerProvider)
				: "<UNKNOWN>";
		} else {
			return CombineByPascalCase(
				provider.GetType().Name
					.Replace(KurrentConfigurationKeys.Prefix, "")
					.Replace("ConfigurationProvider", "")
			);
		}
	}

	private static string GetHelpText() {
		const string OPTION = nameof(OPTION);
		const string DESCRIPTION = nameof(DESCRIPTION);

		var optionColumnWidth = Options().Max(o =>
			OptionHeaderColumnWidth(o.Name, DefaultValue(o)));

		var header = $"{OPTION.PadRight(optionColumnWidth, ' ')}{DESCRIPTION}";

		var environmentOnlyOptions = OptionSections.SelectMany(section => section.GetProperties())
			.Where(option => option.GetCustomAttribute<EnvironmentOnlyAttribute>() != null)
			.Select(option => option)
			.ToList();

		var environmentOnlyOptionsBuilder = environmentOnlyOptions
			.Aggregate(new StringBuilder(),
				(builder, property) => builder.Append(GetEnvironmentOption(property, optionColumnWidth)).AppendLine())
			.ToString();

		var options = Options().Where(option =>
			environmentOnlyOptions.All(environmentOption => environmentOption.Name != option.Name)).ToList();

		var optionGroups = options.GroupBy(option =>
			option.DeclaringType?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty);

		return optionGroups
			.Aggregate(new StringBuilder().Append(header).AppendLine(), (builder, optionGroup) =>
				optionGroup.Aggregate(
					builder.AppendLine().Append(optionGroup.Key).AppendLine(),
					(stringBuilder, property) => stringBuilder.Append(Line(property)).AppendLine()))
			.AppendLine().AppendLine("EnvironmentOnly Options").Append(environmentOnlyOptionsBuilder)
			.ToString();


		string Line(PropertyInfo property) {
			var description = property.GetCustomAttribute<DescriptionAttribute>()?.Description;
			if (property.PropertyType.IsEnum) {
				description += $" ({string.Join(", ", Enum.GetNames(property.PropertyType))})";
			}

			return GetOption(property).PadRight(optionColumnWidth, ' ') + description;
		}

		string GetOption(PropertyInfo property) {
			var builder = new StringBuilder();
			builder.AppendJoin(string.Empty, GnuOption(property.Name));

			var defaultValue = DefaultValue(property);
			if (defaultValue != string.Empty) {
				builder.Append(" (Default:").Append(defaultValue).Append(')');
			}

			return builder.ToString();
		}

		static IEnumerable<PropertyInfo> Options() => OptionSections.SelectMany(type => type.GetProperties());

		static int OptionWidth(string name, string @default) =>
			(name + @default).Count(char.IsUpper) + 1 + 1 + (name + @default).Length;

		int OptionHeaderColumnWidth(string name, string @default) =>
			Math.Max(OptionWidth(name, @default) + 1, OPTION.Length);

		static string DefaultValue(PropertyInfo option) {
			var value = option.GetValue(Activator.CreateInstance(option.DeclaringType!));
			return (value, RuntimeInformation.IsWindows) switch {
				(bool b, false) => b.ToString().ToLower(),
				(bool b, true) => b.ToString(),
				(Array {Length: 0}, _) => string.Empty,
				(Array {Length: >0} a, _) => string.Join(",", a.OfType<object>()),
				_ => value?.ToString() ?? string.Empty
			};
		}

		static IEnumerable<char> GnuOption(string x) {
			yield return '-';
			foreach (var c in x) {
				if (char.IsUpper(c)) {
					yield return '-';
				}

				yield return char.ToLower(c);
			}
		}

		static string GetEnvironmentOption(PropertyInfo property, int optionColumnWidth) {
			var prefix = KurrentConfigurationKeys.Prefix.ToUpper();

			var builder = new StringBuilder();

			builder.Append($"{prefix}_")
				.Append(CombineByPascalCase(property.Name, "_").ToUpper());

			var description = property.GetCustomAttribute<EnvironmentOnlyAttribute>()?.Message;

			return builder.ToString().PadRight(optionColumnWidth, ' ') + description;

		}
	}

	static string CombineByPascalCase(string name, string token = " ") {
		var regex = new System.Text.RegularExpressions.Regex(
			@"(?<=[A-Z])(?=[A-Z][a-z])|(?<=[^A-Z])(?=[A-Z])|(?<=[A-Za-z])(?=[^A-Za-z])");
		return regex.Replace(name, token);
	}

	[AttributeUsage(AttributeTargets.Property)]
	internal class OptionGroupAttribute : Attribute;
}
