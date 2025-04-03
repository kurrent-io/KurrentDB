// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace KurrentDB.TestClient.Commands.RunTestScenarios;

internal class LoopingScenario : ScenarioBase {
	private readonly TimeSpan _executionPeriod;

	private readonly Random _rnd = new Random();
	private volatile bool _stopParalleWrites;
	private TimeSpan _startupWaitInterval;

	protected override TimeSpan StartupWaitInterval {
		get { return _startupWaitInterval; }
	}

	public LoopingScenario(Action<IPEndPoint, byte[]> directSendOverTcp,
		int maxConcurrentRequests,
		int connections,
		int streams,
		int eventsPerStream,
		int streamDeleteStep,
		TimeSpan executionPeriod,
		string dbParentPath,
		NodeConnectionInfo customNodeConnection)
		: base(directSendOverTcp, maxConcurrentRequests, connections, streams, eventsPerStream, streamDeleteStep,
			dbParentPath, customNodeConnection) {
		_executionPeriod = executionPeriod;
		SetStartupWaitInterval(TimeSpan.FromSeconds(10));
	}

	private void SetStartupWaitInterval(TimeSpan interval) {
		_startupWaitInterval = interval;
	}

	protected override void RunInternal() {
		var stopWatch = Stopwatch.StartNew();

		var runIndex = 0;
		while (stopWatch.Elapsed < _executionPeriod) {
			var msg = string.Format(
				"=================== Start run #{0}, elapsed {1} of {2} minutes, {3} =================== ",
				runIndex,
				(int)stopWatch.Elapsed.TotalMinutes,
				_executionPeriod.TotalMinutes,
				GetType().Name);
			Log.Information(
				"=================== Start run #{runIndex}, elapsed {elapsed} of {executionPeriod} minutes, {type} =================== ",
				runIndex,
				(int)stopWatch.Elapsed.TotalMinutes,
				_executionPeriod.TotalMinutes,
				GetType().Name);
			Log.Information("##teamcity[message '{message}']", msg);

			SetStartupWaitInterval(TimeSpan.FromSeconds(10 + (2 * (runIndex % 200))));
			InnerRun(runIndex);
			runIndex += 1;
		}
	}

	protected virtual void InnerRun(int runIndex) {
		var nodeProcessId = StartNode();

		var parallelWritesTimeout = TimeSpan.FromMinutes((EventsPerStream / 1000.0) * 10);

		var parallelWriteTask = RunParallelWrites(runIndex);

		var streams = Enumerable.Range(0, Streams).Select(i => FormatStreamName(runIndex, i)).ToArray();

		var slices = Split(streams, 3);
		var singleEventSlice = slices.Take(1).SelectMany(x => x).ToArray();
		var batchSlice = slices.Skip(1).Take(1).SelectMany(x => x).ToArray();
		var transSlice = slices.Skip(2).Take(1).SelectMany(x => x).ToArray();

		var wr1 = Write(WriteMode.SingleEventAtTime, singleEventSlice, EventsPerStream);
		var wr2 = Write(WriteMode.Bucket, batchSlice, EventsPerStream);
		var wr3 = Write(WriteMode.Transactional, transSlice, EventsPerStream);

		if (runIndex % 4 == 0)
			Scavenge();

		Task.WaitAll(wr1, wr2, wr3);

		var deleted = streams.Where((s, i) => i % StreamDeleteStep == 0).ToArray();
		DeleteStreams(deleted);

		_stopParalleWrites = true;
		if (!parallelWriteTask.Wait(parallelWritesTimeout))
			throw new ApplicationException("Parallel writes stop timed out, 1.");

		KillNode(nodeProcessId);
		nodeProcessId = StartNode();

		parallelWriteTask = RunParallelWrites(runIndex);

		var dl1 = CheckStreamsDeleted(deleted);

		var exceptDeleted = streams.Except(deleted).ToArray();

		var readCnt = Math.Min(100, EventsPerStream / 3);
		var rd1 = Read(exceptDeleted, from: 0, count: readCnt);
		var rd2 = Read(exceptDeleted, from: EventsPerStream - readCnt, count: readCnt);
		var rd3 = Read(exceptDeleted, from: EventsPerStream / 2,
			count: Math.Min(readCnt, EventsPerStream - EventsPerStream / 2));

		Log.Information("== READ from picked ALL ==");

		var allExistingStreamsSlice = (from run in Enumerable.Range(0, runIndex + 1)
			from streamNum in Enumerable.Range(0, Streams)
			where streamNum % StreamDeleteStep != 0
			where _rnd.NextDouble() < 0.1
			select FormatStreamName(run, streamNum)).ToArray();

		var allDeletedStreamsSlice = (from run in Enumerable.Range(0, runIndex + 1)
			from streamNum in Enumerable.Range(0, Streams)
			where streamNum % StreamDeleteStep == 0
			where _rnd.NextDouble() < 0.1
			select FormatStreamName(run, streamNum)).ToArray();

		var prevCheckTasks = new List<Task>();
		if (allExistingStreamsSlice.Length > 0) {
			var rd4 = Read(allExistingStreamsSlice, 0, Math.Max(1, EventsPerStream / 5));
			var rd5 = Read(allExistingStreamsSlice, EventsPerStream / 2, Math.Max(1, EventsPerStream / 5));

			prevCheckTasks.Add(rd4);
			prevCheckTasks.Add(rd5);
		}

		if (allDeletedStreamsSlice.Length > 0) {
			var dl2 = CheckStreamsDeleted(allDeletedStreamsSlice);
			prevCheckTasks.Add(dl2);
		}

		Task.WaitAll(new[] {dl1, rd1, rd2, rd3}.Union(prevCheckTasks).ToArray());

		_stopParalleWrites = true;
		if (!parallelWriteTask.Wait(parallelWritesTimeout))
			throw new ApplicationException("Parallel writes stop timed out, 2.");

		KillNode(nodeProcessId);
	}

	protected virtual Task RunParallelWrites(int runIndex) {
		_stopParalleWrites = false;

		return Task.Factory.StartNew(() => {
			int index = 0;
			while (!_stopParalleWrites) {
				Log.Debug("Start RunParallelWrites #{index} for runIndex {runIndex}", index, runIndex);

				var parallelStreams = Enumerable.Range(0, 2)
					.Select(x => string.Format("parallel-write-stream-in{0}-{1}-{2}",
						runIndex,
						x,
						string.Format("rnd{0}-{1}", _rnd.Next(), DateTime.UtcNow.Ticks)))
					.ToArray();

				var wr = Write(WriteMode.SingleEventAtTime, parallelStreams, EventsPerStream);
				wr.Wait();

				Log.Debug("Wrote RunParallelWrites #{index} for runIndex {runIndex}", index, runIndex);

				var rd1 = Read(parallelStreams, 0, EventsPerStream / 6);
				var rd2 = Read(parallelStreams, EventsPerStream / 3, EventsPerStream / 6);
				var rd3 = Read(parallelStreams, EventsPerStream - EventsPerStream / 10, EventsPerStream / 10);
				Task.WaitAll(rd1, rd2, rd3);

				Log.Debug("Done RunParallelWrites #{index} for runIndex {runIndex}", index, runIndex);

				index += 1;
			}
		}, TaskCreationOptions.LongRunning);
	}

	protected static string FormatStreamName(int runIndex, int i) {
		return string.Format("stream-in{0}-{1}", runIndex, i);
	}
}
