// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;

namespace EventStore.Core;

public static class OptionsFormatter {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(OptionsFormatter));
	public static void LogConfig<TOptions>(string name, TOptions conf) {
		var jsonSerializerSettings = new JsonSerializerSettings {
			NullValueHandling = NullValueHandling.Ignore,
		};
		jsonSerializerSettings.Converters.Add(new StringEnumConverter());

		var confJson = JsonConvert.SerializeObject(
			conf,
			Formatting.Indented,
			jsonSerializerSettings);

		Log.Information(
			Environment.NewLine +
			name +" Configuration: " + Environment.NewLine +
			confJson);
	}
}
