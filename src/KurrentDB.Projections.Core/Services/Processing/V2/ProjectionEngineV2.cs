// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class ProjectionEngineV2 : IAsyncDisposable {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProjectionEngineV2>();

	private readonly ProjectionEngineV2Config _config;
	private readonly IReadStrategy _readStrategy;
	private CancellationTokenSource _cts;
	private Task _runTask;

	public ProjectionEngineV2(ProjectionEngineV2Config config, IReadStrategy readStrategy) {
		_config = config ?? throw new ArgumentNullException(nameof(config));
		_readStrategy = readStrategy ?? throw new ArgumentNullException(nameof(readStrategy));
	}

	public Task Start(TFPos checkpoint, CancellationToken ct) {
		_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		_runTask = Task.Run(() => Run(checkpoint, _cts.Token), _cts.Token);
		return Task.CompletedTask;
	}

	public bool IsFaulted => _runTask?.IsFaulted ?? false;
	public Exception FaultException => _runTask?.Exception?.InnerException;

	private async Task Run(TFPos checkpoint, CancellationToken ct) {
		Log.Information("ProjectionEngineV2 {Name} starting from {Checkpoint}", _config.ProjectionName, checkpoint);
		// Pipeline will be built in subsequent tasks
		await Task.CompletedTask;
	}

	public async ValueTask DisposeAsync() {
		if (_cts is not null) {
			await _cts.CancelAsync();
			if (_runTask is not null) {
				try { await _runTask; } catch (OperationCanceledException) { }
			}
			_cts.Dispose();
		}
		await _readStrategy.DisposeAsync();
	}
}
