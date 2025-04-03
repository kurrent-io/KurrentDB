// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace EventStore.OtlpExporterPlugin.Tests;

public class FakeCollector(TaskCompletionSource<ExportMetricsServiceRequest> tcs) : MetricsService.MetricsServiceBase {
	public override Task<ExportMetricsServiceResponse> Export(ExportMetricsServiceRequest request, ServerCallContext context) {
		tcs.TrySetResult(request);
		return Task.FromResult(new ExportMetricsServiceResponse());
	}
}
