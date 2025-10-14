// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.Extensions.Configuration;
using static KurrentDB.Common.Configuration.ConfigConstants;

namespace KurrentDB.Common.Configuration;

public static class OpenTelemetryConfig {
	public static bool OtlpMetricsEnabled(this IConfiguration config) => config.GetSection(OtlpConfigPrefix).Exists();

	public static bool OtlpLogsEnabled(this IConfiguration config) => config.GetValue<bool>($"{OtlpLogsPrefix}:Enabled");
}
