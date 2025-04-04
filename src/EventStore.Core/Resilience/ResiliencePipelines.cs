// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using Polly;
using Serilog;

namespace EventStore.Core.Resilience;

public static class ResiliencePipelines {
	public static ResiliencePipeline RetrySlow { get; } = BuildRetryPipeline(maxRetryAttempts: 5);

	public static ResiliencePipeline RetryForever { get; } = BuildRetryPipeline(maxRetryAttempts: int.MaxValue);

	private static ResiliencePipeline BuildRetryPipeline(int maxRetryAttempts) =>
		new ResiliencePipelineBuilder()
			.AddRetry(new() {
				BackoffType = DelayBackoffType.Exponential,
				Delay = TimeSpan.FromSeconds(1),
				MaxDelay = TimeSpan.FromSeconds(60),
				MaxRetryAttempts = maxRetryAttempts,
				OnRetry = args => {
					var logger = args.Context.Properties.TryGetValue(ResilienceKeys.Logger, out var log)
						? log
						: Log.Logger;

					logger.Warning(
						"Retrying {Operation}. Attempt {Attempt}/{MaxAttempts}. Duration {Duration}. Delay {Delay}. " +
						"Error: {ExceptionMessage} ({ExceptionType})",
						args.Context.OperationKey,
						args.AttemptNumber,
						maxRetryAttempts == int.MaxValue ? "Infinite" : maxRetryAttempts,
						args.Duration,
						args.RetryDelay,
						args.Outcome.Exception?.Message,
						args.Outcome.Exception?.GetType().Name);

					return ValueTask.CompletedTask;
				},
			})
			.Build();
}
