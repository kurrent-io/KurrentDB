// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Linq;
using KurrentDB.Common.Exceptions;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.Common.Configuration;

public static class ConfigurationRootExtensions {
	private static readonly string[] INVALID_DELIMITERS = [";", "\t"];

	public static string[] GetCommaSeparatedValueAsArray(this IConfiguration configuration, string key) {
		var value = configuration.GetValue<string?>(key);
		if (string.IsNullOrEmpty(value)) {
			return [];
		}

		foreach (var invalidDelimiter in INVALID_DELIMITERS) {
			if (value.Contains(invalidDelimiter)) {
				throw new ArgumentException($"Invalid delimiter {invalidDelimiter} for {key}");
			}
		}

		return value.Split(',', StringSplitOptions.RemoveEmptyEntries);
	}

	public static T BindOptions<T>(this IConfiguration configuration) where T : new() {
		try {
			return configuration.Get<T>() ?? new T();
		} catch (InvalidOperationException ex) {
			var messages = new[] { ex.Message, ex.InnerException?.Message }
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.Select(x => x?.TrimEnd('.'));

			throw new InvalidConfigurationException(string.Join(". ", messages) + ".");
		}
	}
}
