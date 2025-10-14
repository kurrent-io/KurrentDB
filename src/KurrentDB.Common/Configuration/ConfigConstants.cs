// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Common.Configuration;

public static class ConfigConstants {
	public const string RootPrefix = "KurrentDB";
	public const string OpenTelemetryPrefix = $"{RootPrefix}:OpenTelemetry";
	public const string OtlpConfigPrefix = $"{OpenTelemetryPrefix}:Otlp";
	public const string OtlpLogsPrefix = $"{OpenTelemetryPrefix}:Logs";
	public const string OtlpMetricsPrefix = $"{OpenTelemetryPrefix}:Metrics";
}
