// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class LoopingProjectionKillScenario : ProjectionsKillScenario {
	private TimeSpan _executionPeriod;

	private int _iterationCode;
	private readonly TimeSpan _iterationLoopDuration;
	private readonly TimeSpan _firstKillInterval;

	public LoopingProjectionKillScenario(Action<IPEndPoint, byte[]> directSendOverTcp, int maxConcurrentRequests,
		int connections, int streams, int eventsPerStream, int streamDeleteStep, TimeSpan executionPeriod,
		string dbParentPath, NodeConnectionInfo customNode)
		: base(directSendOverTcp, maxConcurrentRequests, connections, streams, eventsPerStream, streamDeleteStep,
			dbParentPath, customNode) {
		_executionPeriod = executionPeriod;

		_iterationLoopDuration = TimeSpan.FromMilliseconds(10 * (Streams * EventsPerStream + Streams) + 20 * 1000);
		_firstKillInterval = TimeSpan.FromSeconds(_iterationLoopDuration.TotalSeconds / 2);
	}

	protected override int GetIterationCode() {
		return _iterationCode;
	}

	protected void SetNextIterationCode() {
		_iterationCode += 1;
	}

	protected override void RunInternal() {
		var nodeProcessId = StartNode();
		EnableProjectionByCategory();

		var stopWatch = Stopwatch.StartNew();

		while (stopWatch.Elapsed < _executionPeriod) {
			var msg = string.Format(
				"=================== Start run #{0}, elapsed {1} of {2} minutes, {3} =================== ",
				GetIterationCode(),
				(int)stopWatch.Elapsed.TotalMinutes,
				_executionPeriod.TotalMinutes,
				GetType().Name);
			Log.Information(
				"=================== Start run #{iteration}, elapsed {elapsed} of {executionPeriod} minutes, {type} =================== ",
				GetIterationCode(),
				(int)stopWatch.Elapsed.TotalMinutes,
				_executionPeriod.TotalMinutes,
				GetType().Name);
			Log.Information("##teamcity[message '{message}']", msg);

			var iterationTask = RunIteration();

			Thread.Sleep(_firstKillInterval);

			KillNode(nodeProcessId);
			nodeProcessId = StartNode();

			if (!iterationTask.Wait(_iterationLoopDuration))
				throw new TimeoutException("Iteration execution timeout.");

			if (iterationTask.Result != true)
				throw new ApplicationException("Iteration faulted.", iterationTask.Exception);

			SetNextIterationCode();
		}
	}

	private Task<bool> RunIteration() {
		var countItem = CreateCountItem();
		var sumCheckForBankAccount0 = CreateSumCheckForBankAccount0();

		var writeTask = WriteData();

		var expectedAllEventsCount = (Streams * EventsPerStream).ToString();
		var lastExpectedEventVersion = (EventsPerStream - 1).ToString();

		var successTask = Task.Factory.StartNew(() => {
			var success = false;
			var stopWatch = new Stopwatch();
			while (stopWatch.Elapsed < _iterationLoopDuration) {
				if (writeTask.IsFaulted)
					throw new ApplicationException("Failed to write data");

				if (writeTask.IsCompleted && !stopWatch.IsRunning) {
					stopWatch.Start();
				}

				success = CheckProjectionState(countItem, "count", x => x == expectedAllEventsCount)
				          && CheckProjectionState(sumCheckForBankAccount0, "success",
					          x => x == lastExpectedEventVersion);

				if (success)
					break;

				Thread.Sleep(4000);
			}

			if (!CheckProjectionState(countItem, "count", x => x == expectedAllEventsCount))
				Log.Error(
					"Projection '{projection}' has not completed with expected result {expectedCount} in time. ",
					countItem, expectedAllEventsCount);

			if (!CheckProjectionState(sumCheckForBankAccount0, "success", x => x == lastExpectedEventVersion))
				Log.Error(
					"Projection '{projection}' has not completed with expected result {lastExpectedEventVersion} in time.",
					sumCheckForBankAccount0, lastExpectedEventVersion);

			return success;
		});

		writeTask.Wait();

		return successTask;
	}
}
