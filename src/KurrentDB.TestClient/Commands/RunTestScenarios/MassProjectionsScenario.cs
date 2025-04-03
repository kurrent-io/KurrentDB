// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class MassProjectionsScenario : ProjectionsKillScenario {
	private readonly Random _random = new Random();

	public MassProjectionsScenario(Action<IPEndPoint, byte[]> directSendOverTcp, int maxConcurrentRequests,
		int threads, int streams, int eventsPerStream, int streamDeleteStep, string dbParentPath,
		NodeConnectionInfo customNode)
		: base(directSendOverTcp, maxConcurrentRequests, threads, streams, eventsPerStream, streamDeleteStep,
			dbParentPath, customNode) {
	}

	protected override void RunInternal() {
		var success = true;

		var nodeProcessId = StartNode();
		EnableProjectionByCategory();

		var countProjections = new List<string>();
		var bankProjections = new List<string>();

		var writeTasks = new List<Task>();

		while (GetIterationCode() < Streams / 3) {
			writeTasks.Add(WriteData());

			SetNextIterationCode();

			countProjections.Add(CreateCountItem());
			bankProjections.Add(CreateSumCheckForBankAccount0());

			Log.Information("Created {bankProjections} and {countProjections}", bankProjections[bankProjections.Count - 1],
				countProjections[countProjections.Count - 1]);
		}

		KillNode(nodeProcessId);
		nodeProcessId = StartNode();

		writeTasks.Add(WriteData());

		var writeTask = Task.Factory.ContinueWhenAll(writeTasks.ToArray(), tsks => Log.Information("All Data written"));

		KillNode(nodeProcessId);
		nodeProcessId = StartNode();

		int count = 10;
		while (count > 0) {
			Log.Information("Stop and start projection, remaining iterations {count}, waiting for data to be written.",
				count);

			StartOrStopProjection(countProjections, false);
			StartOrStopProjection(bankProjections, false);

			Thread.Sleep(TimeSpan.FromSeconds(10));

			StartOrStopProjection(countProjections, true);
			StartOrStopProjection(bankProjections, true);

			if (writeTask.IsCompleted)
				count -= 1;

			if (writeTask.IsFaulted)
				throw new ApplicationException("Failed to write data", writeTask.Exception);

			success = CheckProjectionState(bankProjections[bankProjections.Count - 1],
				"success",
				x => x == (EventsPerStream - 1).ToString());
			if (success)
				break;

			var sleepTimeSeconds = 10 + Streams * EventsPerStream / 1000.0;
			Log.Information("Sleep 1 for {sleepTime} seconds, remaining count {count}", sleepTimeSeconds, count);
			Thread.Sleep(TimeSpan.FromSeconds(sleepTimeSeconds));
		}

		writeTask.Wait();

		count = 20;
		success = false;
		while (!success && count > 0) {
			Log.Information("Wait until projections are computed, remaining iterations {count}", count);
			KillNode(nodeProcessId);
			nodeProcessId = StartNode();

			success = CheckProjectionState(bankProjections[bankProjections.Count - 1],
				"success",
				x => x == (EventsPerStream - 1).ToString());

			if (success)
				break;

			var sleepTimeSeconds = 10 + (Streams * EventsPerStream) / 500;
			Log.Information("Sleep 2 for {sleepTime} seconds, remaining count {count}", sleepTimeSeconds, count);
			Thread.Sleep(TimeSpan.FromSeconds(sleepTimeSeconds));

			count -= 1;
		}

		if (!success)
			throw new ApplicationException("Last bank projection failed");
	}
	
	private void StartOrStopProjection(IEnumerable<string> projections, bool enable) {
		var manager = GetProjectionsManager();
		const int retriesNumber = 5;

		foreach (var projection in projections.ToArray()) {
			var retry = 0;

			int shortWait = 50 + _random.Next(100);
			Log.Debug("Wait for {waitTime}ms before next enable/disable projection", shortWait);
			Thread.Sleep(shortWait);

			while (retry <= retriesNumber) {
				var isRunning = GetProjectionIsRunning(projection);

				try {
					if (enable) {
						if (!isRunning)
							manager.EnableAsync(projection, AdminCredentials).Wait();
						else
							Log.Information("Projection '{projection}' is already running and will not be enabled.",
								projection);
					} else {
						if (isRunning)
							manager.DisableAsync(projection, AdminCredentials).Wait();
						else
							Log.Information(
								"Projection '{projection}' is already not running and will not be disabled again.",
								projection);
					}

					break;
				} catch (Exception ex) {
					var waitForMs = 5000 + (retry * (3000 + _random.Next(2000)));

					Log.Information(ex,
						"Failed to StartOrStopProjection (enable:{enable}; isRunning:{isRunning}) projection {projection}, retry #{retry}, wait {waitTime}ms",
						enable,
						isRunning,
						projection,
						retry,
						waitForMs);

					if (retry != 0)
						Thread.Sleep(waitForMs);

					if (retry == retriesNumber)
						throw new ApplicationException(string.Format(
								"Failed to StartOrStopProjection (enable:{0}) projection {1}," +
								" max number ({2}) of retries reached.",
								enable,
								projection,
								retry),
							ex);
				}


				retry++;
			}
		}
	}

	private int _iterationCode;

	protected override int GetIterationCode() {
		return _iterationCode;
	}

	private void SetNextIterationCode() {
		_iterationCode += 1;
	}
}
