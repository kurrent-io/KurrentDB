// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using EventStore.Common.Configuration;
using Serilog;

namespace EventStore.Core.Metrics;

public class MessageLabelConfigurator {
	private static readonly ILogger Log = Serilog.Log.ForContext<MessageLabelConfigurator>();

	public static void ConfigureMessageLabels(
		MetricsConfiguration.LabelMappingCase[] configuration,
		IEnumerable<Type> messageTypes) {

		var labels = new HashSet<string>();

		foreach (var messageType in messageTypes) {
			if (TryConfigureMessageType(configuration, messageType, out var label)) {
				labels.Add(label);
			}
		}

		Log.Information("Metrics created {count} message type labels", labels.Count);
	}

	private static bool TryConfigureMessageType(
		MetricsConfiguration.LabelMappingCase[] configuration,
		Type messageType,
		out string label) {

		label = default;

		if (messageType.IsAbstract)
			return false;

		var labelStaticProperty = messageType
			.GetProperty("LabelStatic", BindingFlags.Static | BindingFlags.Public);

		if (labelStaticProperty is null) {
			Log.Warning($"{messageType} may be missing the DerivedMessage attribute.");
			return false;
		}

		if (labelStaticProperty.GetValue(null) is not string oldLabel) {
			oldLabel = "";
		}

		foreach (var @case in configuration) {
			var pattern = $"^{@case.Regex}$";
			var match = Regex.Match(input: oldLabel, pattern: pattern);
			if (match.Success) {
				if (string.IsNullOrWhiteSpace(@case.Label)) {
					Log.Warning(
						"Label for message {message} matching pattern {pattern} was not specified.",
						oldLabel, @case.Regex);
					label = oldLabel;
					return true;
				}

				label = Regex.Replace(
					input: oldLabel,
					pattern: pattern,
					replacement: @case.Label);

				labelStaticProperty.SetValue(null, label);

				Log.Verbose(
					"Metrics matched message {old} with pattern {pattern} and set it to {new}",
					oldLabel, @case.Regex, label);

				return true;
			}
		}

		label = oldLabel;
		return true;
	}
}
