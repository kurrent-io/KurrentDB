using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;

#nullable enable
namespace EventStore.Common.Configuration {
	public static class ConfigurationRootExtensions {
		private static string[] INVALID_DELIMITERS = new[] { ";", "\t" };

		public static string[] GetCommaSeparatedValueAsArray(this IConfigurationRoot configurationRoot, string key) {
			string? value = configurationRoot.GetValue<string?>(key);
			if (string.IsNullOrEmpty(value)) {
				return Array.Empty<string>();
			}

			foreach (var invalidDelimiter in INVALID_DELIMITERS) {
				if (value.Contains(invalidDelimiter)) {
					throw new ArgumentException($"Invalid delimiter {invalidDelimiter} for {key}");
				}
			}

			return value.Split(',', StringSplitOptions.RemoveEmptyEntries);
		}

		// has the user specified what they want, or left it up to the default.
		// specifying the same value as the default still counts as being used specified.
		public static bool IsUserSpecified(this IConfigurationRoot configurationRoot, string key) {
			foreach (var provider in configurationRoot.Providers) {
				if (provider.GetType() == typeof(Default))
					continue;

				if (provider.TryGet(key, out _)) {
					return true;
				}
			}

			return false;
		}

		public static string? CheckProvidersForEnvironmentVariables(IConfigurationRoot? configurationRoot, IEnumerable<Type> OptionSections) {
			if (configurationRoot != null) {
				var environmentOptionsOnly = OptionSections.SelectMany(section => section.GetProperties())
					.Where(option => option.GetCustomAttribute<EnvironmentOnlyAttribute>() != null)
					.Select(option => option)
					.ToArray();

				var errorBuilder = new StringBuilder();

				foreach (var provider in configurationRoot.Providers) {
					var source = provider.GetType();

					if (source == typeof(Default) || source == typeof(EnvironmentVariables))
						continue;

					var errorDescriptions = from key in provider.GetChildKeys(Enumerable.Empty<string>(), default)
											from property in environmentOptionsOnly
											where string.Equals(property.Name, key, StringComparison.CurrentCultureIgnoreCase)
											select property.GetCustomAttribute<EnvironmentOnlyAttribute>()?.Message;

					var builder = errorDescriptions
						.Aggregate(new StringBuilder(), (stringBuilder, errorDescription) => stringBuilder.AppendLine($"Provided by: {provider.GetType().Name}. {errorDescription}"));
					errorBuilder.Append(builder);
				}
				return errorBuilder.Length != 0 ? errorBuilder.ToString() : null;
			}
			return null;
		}

		public static string GetEnvironmentOption(PropertyInfo property, int optionColumnWidth) {
			var builder = new StringBuilder();
			const string Prefix = "EVENTSTORE";

			builder.Append($"{Prefix}_")
				.Append(OptionsDumper.NameTranslators.CombineByPascalCase(property.Name, "_").ToUpper());
			var description = property.GetCustomAttribute<EnvironmentOnlyAttribute>()?.Message;

			return builder.ToString().PadRight(optionColumnWidth, ' ') + description;
		}

		public static string GetString(this IConfigurationRoot configurationRoot, string key) {
			return configurationRoot.GetValue<string>(key) ?? string.Empty;
		}
	}
}
