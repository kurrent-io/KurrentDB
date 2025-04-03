// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class ProjectionWrongTagCheck : ProjectionsKillScenario {
	private TimeSpan _executionPeriod;

	public ProjectionWrongTagCheck(Action<IPEndPoint, byte[]> directSendOverTcp, int maxConcurrentRequests,
		int connections, int streams, int eventsPerStream, int streamDeleteStep, TimeSpan executionPeriod,
		string dbParentPath, NodeConnectionInfo customNode)
		: base(directSendOverTcp, maxConcurrentRequests, connections, streams, eventsPerStream, streamDeleteStep,
			dbParentPath, customNode) {
		_executionPeriod = executionPeriod;
	}

	private int _iterationCode = 0;

	protected override int GetIterationCode() {
		return _iterationCode;
	}

	protected void SetNextIterationCode() {
		_iterationCode += 1;
	}

	protected override void RunInternal() {
		var nodeProcessId = StartNode();
		EnableProjectionByCategory();
		KillNode(nodeProcessId);

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
			iterationTask.Wait();

			SetNextIterationCode();
		}
	}

	private Task RunIteration() {
		var nodeProcessId = StartNode();


		var sumCheckForBankAccount0 = CreateSumCheckForBankAccount0();

		var writeTask = WriteData();

		var lastExpectedEventVersion = (EventsPerStream - 1).ToString();

		var successTask = Task.Factory.StartNew<bool>(() => {
			var success = true;
			var stopWatch = new Stopwatch();

			var wasLessLastTime = false;
			while (stopWatch.Elapsed < TimeSpan.FromMilliseconds(10 * (Streams * EventsPerStream + Streams))) {
				if (!wasLessLastTime) {
					KillNode(nodeProcessId);
					nodeProcessId = StartNode();
				}

				if (writeTask.IsFaulted)
					throw new ApplicationException("Failed to write data");

				if (writeTask.IsCompleted && !stopWatch.IsRunning) {
					stopWatch.Start();
				}

				var count1 = GetProjectionStateValue(sumCheckForBankAccount0, "success", int.Parse, -1);
				for (var i = 0; i < 5; ++i) {
					Thread.Sleep(TimeSpan.FromSeconds(1));
					var count2 = GetProjectionStateValue(sumCheckForBankAccount0, "success", int.Parse, -1);

					if (count1 > count2) {
						if (wasLessLastTime) {
							success = false;
							break;
						}

						wasLessLastTime = true;
					}

					count1 = count2;
				}

				if (!success)
					break;

				if (CheckProjectionState(sumCheckForBankAccount0, "success", x => x == lastExpectedEventVersion))
					break;
			}

			KillNode(nodeProcessId);

			if (!success)
				throw new ApplicationException(string.Format(
					"Projection {0} has not completed with expected result {1} in time.", sumCheckForBankAccount0,
					lastExpectedEventVersion));

			return success;
		});

		return Task.Factory.ContinueWhenAll(new[] {writeTask, successTask}, tasks => {
			Log.Information("Iteration {iteration} tasks completed", GetIterationCode());
			Task.WaitAll(tasks);
			Log.Information("Iteration {iteration} successful", GetIterationCode());
		});
	}
}
