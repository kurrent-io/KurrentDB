// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using KurrentDB.TestClient.Commands;
using KurrentDB.TestClient.Statistics;
using EventData = EventStore.ClientAPI.EventData;
using ExpectedVersion = EventStore.Core.Data.ExpectedVersion;
using StreamDeletedException = EventStore.ClientAPI.Exceptions.StreamDeletedException;
using WrongExpectedVersionException = EventStore.ClientAPI.Exceptions.WrongExpectedVersionException;

namespace KurrentDB.TestClient.ClientApiTcpCommands;

internal class WriteFloodProcessor : ICmdProcessor {
	public string Usage {
		//                        0          1            2           3          4              5
		get { return "WRFLTCP [<clients> <requests> [<streams-cnt> [<size> [<batchsize> [<stream-prefix>]]]]]"; }
	}

	public string Keyword {
		get { return "WRFLTCP"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		long requestsCnt = 5000;
		int streamsCnt = 1000;
		int size = 256;
		int batchSize = 1;
		string streamPrefix = "";
		if (args.Length > 0) {
			if (args.Length < 2 || args.Length > 6)
				return false;

			try {
				clientsCnt = MetricPrefixValue.ParseInt(args[0]);
				requestsCnt = MetricPrefixValue.ParseLong(args[1]);
				if (args.Length >= 3)
					streamsCnt = MetricPrefixValue.ParseInt(args[2]);
				if (args.Length >= 4)
					size = MetricPrefixValue.ParseInt(args[3]);
				if (args.Length >= 5)
					batchSize = MetricPrefixValue.ParseInt(args[4]);
				if (args.Length >= 6)
					streamPrefix = args[5];
			} catch {
				return false;
			}
		}

		var stats = new WriteFloodStats(Keyword, context.OutputCsv, args);
		var monitor = new RequestMonitor();
		WriteFlood(context, stats, clientsCnt, requestsCnt, streamsCnt, size, batchSize, streamPrefix, monitor)
			.GetAwaiter().GetResult();
		return true;
	}

	private async Task WriteFlood(CommandProcessorContext context, WriteFloodStats stats, int clientsCnt, long requestsCnt, int streamsCnt,
		int size, int batchSize, string streamPrefix, RequestMonitor monitor) {
		context.IsAsync();

		var doneEvent = new ManualResetEventSlim(false);
		var clients = new List<IEventStoreConnection>();

		long last = 0;

		var streams = Enumerable.Range(0, streamsCnt).Select(x =>
			string.IsNullOrWhiteSpace(streamPrefix)
				? Guid.NewGuid().ToString()
				: $"{streamPrefix}-{x}").ToArray();

		context.Log.Information("Writing streams randomly between {first} and {last}",
			streams.FirstOrDefault(),
			streams.LastOrDefault());

		var start = new TaskCompletionSource();
		stats.StartTime = DateTime.UtcNow;
		var sw2 = new Stopwatch();
		var capacity = 2000 / clientsCnt;
		var clientTasks = new List<Task>();
		for (int i = 0; i < clientsCnt; i++) {
			var count = requestsCnt / clientsCnt + ((i == clientsCnt - 1) ? requestsCnt % clientsCnt : 0);

			var client = context._clientApiTestClient.CreateConnection();
			await client.ConnectAsync();
			clientTasks.Add(RunClient(client, count));
		}

		async Task RunClient(IEventStoreConnection client, long count) {
			var rnd = new Random();
			List<Task> pending = new List<Task>(capacity);
			await start.Task;

			for (int j = 0; j < count; ++j) {
				var events = new EventData[batchSize];
				for (int q = 0; q < batchSize; q++) {
					events[q] = new EventData(Guid.NewGuid(),
						"TakeSomeSpaceEvent", false,
						EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(
							"{ \"DATA\" : \"" + new string('*', size) + "\"}"),
						EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(
							"{ \"METADATA\" : \"" + new string('$', 100) + "\"}"));
				}

				var corrid = Guid.NewGuid();
				monitor.StartOperation(corrid);

				pending.Add(client.AppendToStreamAsync(streams[rnd.Next(streamsCnt)], ExpectedVersion.Any, events)
					.ContinueWith(t => {
						monitor.EndOperation(corrid);
						if (t.IsCompletedSuccessfully) Interlocked.Add(ref stats.Succ, batchSize);
						else {
							if (Interlocked.Increment(ref stats.Fail) % 1000 == 0) {
								Console.Write("#");
							}

							if (t.Exception != null) {
								var exception = t.Exception.Flatten();
								switch (exception.InnerException) {
									case WrongExpectedVersionException _:
										Interlocked.Increment(ref stats.WrongExpVersion);
										break;
									case StreamDeletedException _:
										Interlocked.Increment(ref stats.StreamDeleted);
										break;
									case OperationTimedOutException _:
										Interlocked.Increment(ref stats.CommitTimeout);
										break;
								}
							}
						}

						Interlocked.Add(ref stats.Succ, batchSize);
						if (stats.Succ - last > 1000) {
							last = stats.Succ;
							Console.Write(".");
						}

						var localAll = Interlocked.Add(ref stats.All, batchSize);
						if (localAll % 100000 == 0) {
							stats.Elapsed = sw2.Elapsed;
							stats.Rate = 1000.0 * 100000 / stats.Elapsed.TotalMilliseconds;
							sw2.Restart();
							context.Log.Debug(
								"\nDONE TOTAL {writes} WRITES IN {elapsed} ({rate:0.0}/s) [S:{success}, F:{failures} (WEV:{wrongExpectedVersion}, P:{prepareTimeout}, C:{commitTimeout}, F:{forwardTimeout}, D:{streamDeleted})].",
								localAll, stats.Elapsed, stats.Rate,
								stats.Succ, stats.Fail,
								stats.WrongExpVersion, stats.PrepTimeout, stats.CommitTimeout, stats.ForwardTimeout, stats.StreamDeleted);
							stats.WriteStatsToFile(context.StatsLogger);
						}

						if (localAll >= requestsCnt) {
							context.Success();
							doneEvent.Set();
						}
					}));
				if (pending.Count == capacity) {
					await Task.WhenAny(pending);

					while (pending.Count > 0 && Task.WhenAny(pending).IsCompleted) {
						pending.RemoveAll(x => x.IsCompleted);
						if (stats.Succ - last > 1000) {
							Console.Write(".");
							last = stats.Succ;
						}
					}
				}
			}

			if (pending.Count > 0) await Task.WhenAll(pending);
		}

		var sw = Stopwatch.StartNew();
		sw2.Start();
		start.SetResult();
		await Task.WhenAll(clientTasks);
		sw.Stop();

		clients.ForEach(client => client.Close());

		context.Log.Information(
			"Completed. Successes: {success}, failures: {failures} (WRONG VERSION: {wrongExpectedVersion}, P: {prepareTimeout}, C: {commitTimeout}, F: {forwardTimeout}, D: {streamDeleted})",
			stats.Succ, stats.Fail,
			stats.WrongExpVersion, stats.PrepTimeout, stats.CommitTimeout, stats.ForwardTimeout, stats.StreamDeleted);
		stats.WriteStatsToFile(context.StatsLogger);

		var reqPerSec = (stats.All + 0.0) / sw.ElapsedMilliseconds * 1000;
		context.Log.Information("{requests} requests completed in {elapsed}ms ({rate:0.00} reqs per sec).", stats.All,
			sw.ElapsedMilliseconds, reqPerSec);

		PerfUtils.LogData(
			Keyword,
			PerfUtils.Row(PerfUtils.Col("clientsCnt", clientsCnt),
				PerfUtils.Col("requestsCnt", requestsCnt),
				PerfUtils.Col("ElapsedMilliseconds", sw.ElapsedMilliseconds)),
			PerfUtils.Row(PerfUtils.Col("successes", stats.Succ), PerfUtils.Col("failures", stats.Fail)));

		var failuresRate = (int)(100 * stats.Fail / (stats.Fail + stats.Succ));
		PerfUtils.LogTeamCityGraphData(string.Format("{0}-{1}-{2}-reqPerSec", Keyword, clientsCnt, requestsCnt),
			(int)reqPerSec);
		PerfUtils.LogTeamCityGraphData(
			string.Format("{0}-{1}-{2}-failureSuccessRate", Keyword, clientsCnt, requestsCnt), failuresRate);
		PerfUtils.LogTeamCityGraphData(
			string.Format("{0}-c{1}-r{2}-st{3}-s{4}-reqPerSec", Keyword, clientsCnt, requestsCnt, streamsCnt,
				size),
			(int)reqPerSec);
		PerfUtils.LogTeamCityGraphData(
			string.Format("{0}-c{1}-r{2}-st{3}-s{4}-failureSuccessRate", Keyword, clientsCnt, requestsCnt,
				streamsCnt, size), failuresRate);
		monitor.GetMeasurementDetails();
		if (Interlocked.Read(ref stats.Succ) != requestsCnt)
			context.Fail(reason: "There were errors or not all requests completed.");
		else
			context.Success();
	}
}
