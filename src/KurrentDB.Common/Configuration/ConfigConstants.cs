// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Common.Configuration;

public static class ConfigConstants {
	public const string RootPrefix = KurrentDB;
	public const string OtlpConfigPrefix = $"{KurrentDB}:{OpenTelemetry}:{Otlp}";
	public const string OtlpLogsPrefix = $"{KurrentDB}:{OpenTelemetry}:{Logs}";
	public const string OtlpLogsOtlpPrefix = $"{KurrentDB}:{OpenTelemetry}:{Logs}:{Otlp}";
	public const string OtlpMetricsPrefix = $"{KurrentDB}:{OpenTelemetry}:{Metrics}";
	public const string OtlpMetricsOtlpPrefix = $"{KurrentDB}:{OpenTelemetry}:{Metrics}:{Otlp}";

	const string KurrentDB = nameof(KurrentDB);
	const string OpenTelemetry = nameof(OpenTelemetry);
	const string Otlp = nameof(Otlp);
	const string Logs = nameof(Logs);
	const string Metrics = nameof(Metrics);

	public static readonly TimeSpan DefaultSlowMessageThreshold = TimeSpan.FromMilliseconds(48);
}
