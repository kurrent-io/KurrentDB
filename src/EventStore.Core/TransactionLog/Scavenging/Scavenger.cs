// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime.CompilerServices;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using Serilog;

namespace EventStore.Core.TransactionLog.Scavenging;

public class Scavenger<TStreamId> : IScavenger {
	private readonly ILogger _logger;
	private readonly Action _checkPreconditions;
	private readonly IScavengeState<TStreamId> _state;
	private readonly IAccumulator<TStreamId> _accumulator;
	private readonly ICalculator<TStreamId> _calculator;
	private readonly IChunkExecutor<TStreamId> _chunkExecutor;
	private readonly IChunkMerger _chunkMerger;
	private readonly IIndexExecutor<TStreamId> _indexExecutor;
	private readonly ICleaner _cleaner;
	private readonly IScavengePointSource _scavengePointSource;
	private readonly ITFChunkScavengerLog _scavengerLogger;
	private readonly IScavengeStatusTracker _statusTracker;
	private readonly int _thresholdForNewScavenge;
	private readonly bool _syncOnly;
	private readonly Func<string> _getThrottleStats;

	private readonly Dictionary<string, TimeSpan> _recordedTimes =
		new Dictionary<string, TimeSpan>();

	public Scavenger(
		ILogger logger,
		Action checkPreconditions,
		IScavengeState<TStreamId> state,
		IAccumulator<TStreamId> accumulator,
		ICalculator<TStreamId> calculator,
		IChunkExecutor<TStreamId> chunkExecutor,
		IChunkMerger chunkMerger,
		IIndexExecutor<TStreamId> indexExecutor,
		ICleaner cleaner,
		IScavengePointSource scavengePointSource,
		ITFChunkScavengerLog scavengerLogger,
		IScavengeStatusTracker statusTracker,
		int thresholdForNewScavenge,
		bool syncOnly,
		Func<string> getThrottleStats) {

		_logger = logger;
		_checkPreconditions = checkPreconditions;
		_state = state;
		_accumulator = accumulator;
		_calculator = calculator;
		_chunkExecutor = chunkExecutor;
		_chunkMerger = chunkMerger;
		_indexExecutor = indexExecutor;
		_cleaner = cleaner;
		_scavengePointSource = scavengePointSource;
		_scavengerLogger = scavengerLogger;
		_statusTracker = statusTracker;
		_thresholdForNewScavenge = thresholdForNewScavenge;
		_syncOnly = syncOnly;
		_getThrottleStats = getThrottleStats;
	}

	public string ScavengeId => _scavengerLogger.ScavengeId;

	public void Dispose() {
		_state.Dispose();
	}

	// following old scavenging design the returned task must complete successfully
	[AsyncMethodBuilder(typeof(SpawningAsyncTaskMethodBuilder<>))] // get off the main queue
	public async Task<ScavengeResult> ScavengeAsync(CancellationToken cancellationToken) {
		_recordedTimes.Clear();
		var stopwatch = Stopwatch.StartNew();
		var result = ScavengeResult.Success;
		string error = null;
		try {
			_logger.Debug("SCAVENGING: Scavenge Initializing State.");
			_state.Init();
			_state.LogStats();
			_logger.Debug("SCAVENGING: Scavenge Started.");
			LogCollisions();

			_scavengerLogger.ScavengeStarted();

			await RunInternal(_scavengerLogger, stopwatch, cancellationToken);

			_logger.Debug(
				"SCAVENGING: Scavenge Completed. Total time taken: {elapsed}. Total space saved: {spaceSaved}.",
				stopwatch.Elapsed, _scavengerLogger.SpaceSaved);

		} catch (OperationCanceledException) {
			_logger.Information("SCAVENGING: Scavenge Stopped. Total time taken: {elapsed}.",
				stopwatch.Elapsed);
			result = ScavengeResult.Stopped;
		} catch (Exception exc) {
			result = ScavengeResult.Errored;
			_logger.Error(exc, "SCAVENGING: Scavenge Failed. Total time taken: {elapsed}.",
				stopwatch.Elapsed);
			error = string.Format("Error while scavenging DB: {0}.", exc.Message);
		} finally {
			try {
				_scavengerLogger.ScavengeCompleted(result, error, stopwatch.Elapsed);
				LogCollisions();
				LogTimes();
			} catch (Exception ex) {
				_logger.Error(
					ex,
					"SCAVENGING: Error whilst recording scavenge completed. " +
					"Scavenge result: {result}, Elapsed: {elapsed}, Original error: {e}",
					result, stopwatch.Elapsed, error);
			}
		}

		return result;
	}

	private void LogCollisions() {
		var collisions = _state.AllCollisions().ToArray();
		_logger.Debug("SCAVENGING: {count} KNOWN COLLISIONS", collisions.Length);

		foreach (var collision in collisions) {
			_logger.Debug("SCAVENGING: KNOWN COLLISION: \"{collision}\"", collision);
		}
	}

	private async Task RunInternal(
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		_checkPreconditions();

		// each component can be started with either
		//  (i) a checkpoint that it wrote previously (it will continue from there)
		//  (ii) fresh from a given scavengepoint
		//
		// so if we have a checkpoint from a component, start the scavenge by passing the checkpoint
		// into that component and then starting each subsequent component fresh for that
		// scavengepoint.
		//
		// otherwise, start the whole scavenge fresh from whichever scavengepoint is applicable.
		if (!_state.TryGetCheckpoint(out var checkpoint)) {
			// there is no checkpoint, so this is the first scavenge of this scavenge state
			// (not necessarily the first scavenge of this database, old scavenged may have been run
			// or new scavenges run and the scavenge state deleted)
			_logger.Debug("SCAVENGING: Started a new scavenge with no checkpoint");
			await StartNewAsync(
				prevScavengePoint: null,
				scavengerLogger,
				stopwatch,
				cancellationToken);

		} else if (checkpoint is ScavengeCheckpoint.Done done) {
			// start of a subsequent scavenge.
			_logger.Debug("SCAVENGING: Started a new scavenge after checkpoint {checkpoint}", checkpoint);
			await StartNewAsync(
				prevScavengePoint: done.ScavengePoint,
				scavengerLogger,
				stopwatch,
				cancellationToken);

		} else {
			// the other cases are continuing an incomplete scavenge
			_logger.Debug("SCAVENGING: Continuing a scavenge from {checkpoint}", checkpoint);
			switch (checkpoint) {
				case ScavengeCheckpoint.Accumulating accumulating:
					await Time(stopwatch, "Accumulation", cancellationToken =>
							_accumulator.Accumulate(accumulating, _state, cancellationToken)
						, cancellationToken);
					await AfterAccumulation(
						accumulating.ScavengePoint, scavengerLogger, stopwatch, cancellationToken);
					break;
				case ScavengeCheckpoint.Calculating<TStreamId> calculating:
					await Time(stopwatch, "Calculation", cancellationToken =>
							_calculator.Calculate(calculating, _state, cancellationToken),
						cancellationToken);
					await AfterCalculation(
						calculating.ScavengePoint, scavengerLogger, stopwatch, cancellationToken);
					break;
				case ScavengeCheckpoint.ExecutingChunks executingChunks:
					await Time(stopwatch, "Chunk execution", cancellationToken =>
							_chunkExecutor.Execute(executingChunks, _state, scavengerLogger, cancellationToken),
						cancellationToken);
					await AfterChunkExecution(
						executingChunks.ScavengePoint, scavengerLogger, stopwatch, cancellationToken);
					break;
				case ScavengeCheckpoint.MergingChunks mergingChunks:
					await Time(stopwatch, "Chunk merging", cancellationToken =>
							_chunkMerger.MergeChunks(mergingChunks, _state, scavengerLogger, cancellationToken),
						cancellationToken);
					await AfterChunkMerging(
						mergingChunks.ScavengePoint, scavengerLogger, stopwatch, cancellationToken);
					break;
				case ScavengeCheckpoint.ExecutingIndex executingIndex:
					await Time(stopwatch, "Index execution", cancellationToken =>
							_indexExecutor.Execute(executingIndex, _state, scavengerLogger, cancellationToken),
						cancellationToken);
					await AfterIndexExecution(
						executingIndex.ScavengePoint, stopwatch, cancellationToken);
					break;
				case ScavengeCheckpoint.Cleaning cleaning:
					await Time(stopwatch, "Cleaning", cancellationToken => {
						_cleaner.Clean(cleaning, _state, cancellationToken);
						return ValueTask.CompletedTask;
					}, cancellationToken);
					AfterCleaning(cleaning.ScavengePoint);
					break;
				default:
					throw new Exception($"Unexpected checkpoint: {checkpoint}");
			}
		}
	}

	private async ValueTask Time(Stopwatch stopwatch, string name, Func<CancellationToken, ValueTask> f, CancellationToken token) {
		using var _ = _statusTracker.StartActivity(name);
		_logger.Debug("SCAVENGING: Scavenge " + name + " Phase Started.");
		var start = stopwatch.Elapsed;
		await f(token);
		var elapsed = stopwatch.Elapsed - start;
		_state.LogStats();
		_logger.Debug($"SCAVENGING: {_getThrottleStats()}");
		_logger.Debug("SCAVENGING: Scavenge " + name + " Phase Completed. Took {elapsed}.", elapsed);
		_recordedTimes[name] = elapsed;
	}

	private void LogTimes() {
		foreach (var key in _recordedTimes.Keys.OrderBy(x => x)) {
			_logger.Debug("SCAVENGING: {name} took {elapsed}", key, _recordedTimes[key]);
		}
	}

	private async Task StartNewAsync(
		ScavengePoint prevScavengePoint,
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		// prevScavengePoint is the previous one that was completed
		// latestScavengePoint is the latest one in the database
		// nextScavengePoint is the one we are about to scavenge up to

		ScavengePoint nextScavengePoint;
		var latestScavengePoint = await _scavengePointSource
			.GetLatestScavengePointOrDefaultAsync(cancellationToken);
		if (latestScavengePoint == null) {
			if (_syncOnly) {
				_logger.Debug("SCAVENGING: No existing scavenge point to sync with, nothing to do.");
				return;
			} else {
				_logger.Debug("SCAVENGING: Creating the first scavenge point.");
				// no latest scavenge point, create the first one
				nextScavengePoint = await _scavengePointSource
					.AddScavengePointAsync(
						ExpectedVersion.NoStream,
						threshold: _thresholdForNewScavenge,
						cancellationToken);
			}
		} else {
			// got the latest scavenge point
			if (prevScavengePoint == null ||
				prevScavengePoint.EventNumber < latestScavengePoint.EventNumber) {
				// the latest scavengepoint is suitable
				_logger.Debug(
					"SCAVENGING: Using existing scavenge point {scavengePointNumber}",
					latestScavengePoint.EventNumber);
				nextScavengePoint = latestScavengePoint;
			} else {
				if (_syncOnly) {
					_logger.Debug("SCAVENGING: No existing scavenge point to sync with, nothing to do.");
					return;
				} else {
					// the latest scavengepoint is the prev scavenge point, so create a new one
					var expectedVersion = prevScavengePoint.EventNumber;
					_logger.Debug(
						"SCAVENGING: Creating the next scavenge point: {scavengePointNumber}",
						expectedVersion + 1);

					nextScavengePoint = await _scavengePointSource
						.AddScavengePointAsync(
							expectedVersion,
							threshold: _thresholdForNewScavenge,
							cancellationToken);
				}
			}
		}

		// we now have a nextScavengePoint.
		await Time(stopwatch, "Accumulation", cancellationToken =>
				_accumulator.Accumulate(prevScavengePoint, nextScavengePoint, _state, cancellationToken)
			, cancellationToken);
		await AfterAccumulation(nextScavengePoint, scavengerLogger, stopwatch, cancellationToken);
	}

	private async ValueTask AfterAccumulation(
		ScavengePoint scavengepoint,
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		LogCollisions();
		await Time(stopwatch, "Calculation", cancellationToken =>
			_calculator.Calculate(scavengepoint, _state, cancellationToken),
			cancellationToken);
		await AfterCalculation(scavengepoint, scavengerLogger, stopwatch, cancellationToken);
	}

	private async ValueTask AfterCalculation(
		ScavengePoint scavengePoint,
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		await Time(stopwatch, "Chunk execution", cancellationToken =>
			_chunkExecutor.Execute(scavengePoint, _state, scavengerLogger, cancellationToken), cancellationToken);
		await AfterChunkExecution(scavengePoint, scavengerLogger, stopwatch, cancellationToken);
	}

	private async ValueTask AfterChunkExecution(
		ScavengePoint scavengePoint,
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		await Time(stopwatch, "Chunk merging", cancellationToken =>
			_chunkMerger.MergeChunks(scavengePoint, _state, scavengerLogger, cancellationToken), cancellationToken);
		await AfterChunkMerging(scavengePoint, scavengerLogger, stopwatch, cancellationToken);
	}

	private async ValueTask AfterChunkMerging(
		ScavengePoint scavengePoint,
		ITFChunkScavengerLog scavengerLogger,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		await Time(stopwatch, "Index execution", cancellationToken =>
			_indexExecutor.Execute(scavengePoint, _state, scavengerLogger, cancellationToken),
			cancellationToken);
		await AfterIndexExecution(scavengePoint, stopwatch, cancellationToken);
	}

	private async ValueTask AfterIndexExecution(
		ScavengePoint scavengePoint,
		Stopwatch stopwatch,
		CancellationToken cancellationToken) {

		await Time(stopwatch, "Cleaning", cancellationToken => {
			_cleaner.Clean(scavengePoint, _state, cancellationToken);
			return ValueTask.CompletedTask;
		}, cancellationToken);
		AfterCleaning(scavengePoint);
	}

	private void AfterCleaning(ScavengePoint scavengePoint) {
		_state.SetCheckpoint(new ScavengeCheckpoint.Done(scavengePoint));
	}
}
