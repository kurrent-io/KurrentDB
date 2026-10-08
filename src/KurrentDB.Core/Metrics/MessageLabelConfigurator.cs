// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text.RegularExpressions;
using KurrentDB.Common.Configuration;
using Serilog;

namespace KurrentDB.Core.Metrics;

public static class MessageLabelConfigurator {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(MessageLabelConfigurator));

	private static MetricsConfiguration.LabelMappingCase[] _configuration = [];

	// Message labels are resolved lazily on first access (see Message.ResolveLabel), so this must be
	// called before any message label is read. Labels that were already resolved are not affected.
	public static void ConfigureMessageLabels(MetricsConfiguration.LabelMappingCase[] configuration) {
		_configuration = configuration;
		Log.Information("Metrics configured {count} message type label mappings", configuration.Length);
	}

	internal static string ResolveLabel(string originalLabel) => ResolveLabel(originalLabel, _configuration);

	internal static string ResolveLabel(string originalLabel, ReadOnlySpan<MetricsConfiguration.LabelMappingCase> configuration) {
		foreach (var @case in configuration) {
			var pattern = $"^{@case.Regex}$";
			var match = Regex.Match(input: originalLabel, pattern: pattern);
			if (match.Success) {
				if (string.IsNullOrWhiteSpace(@case.Label)) {
					Log.Warning(
						"Label for message {message} matching pattern {pattern} was not specified.",
						originalLabel, @case.Regex);
					return originalLabel;
				}

				var label = Regex.Replace(
					input: originalLabel,
					pattern: pattern,
					replacement: @case.Label);

				Log.Verbose(
					"Metrics matched message {old} with pattern {pattern} and set it to {new}",
					originalLabel, @case.Regex, label);

				return label;
			}
		}

		return originalLabel;
	}
}
