// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Serilog;

namespace EventStore.Core.Resilience;

// Executes a ResiliencePipeline in the context of an operation * logger
public class PipelineExecutor(ResiliencePipeline pipeline, string operationPrefix, ILogger logger) {
	private ResilienceContext GetContext(string operation, CancellationToken ct) {
		var context = ResilienceContextPool.Shared.Get($"{operationPrefix}.{operation}", ct);
		context.Properties.Set(ResilienceKeys.Logger, logger);
		return context;
	}

	private static void Return(ResilienceContext context) =>
		ResilienceContextPool.Shared.Return(context);

	public async ValueTask ExecuteAsync<TState>(
		Func<ResilienceContext, TState, ValueTask> func,
		TState state,
		CancellationToken ct,
		[CallerMemberName] string memberName = "") {

		var context = GetContext(memberName, ct);
		try {
			await pipeline.ExecuteAsync(func, context, state);
		} finally {
			Return(context);
		}
	}

	public async ValueTask<TResult> ExecuteAsync<TResult, TState>(
		Func<ResilienceContext, TState, ValueTask<TResult>> func,
		TState state,
		CancellationToken ct,
		[CallerMemberName] string memberName = "") {

		var context = GetContext(memberName, ct);
		try {
			return await pipeline.ExecuteAsync(func, context, state);
		} finally {
			Return(context);
		}
	}
}

