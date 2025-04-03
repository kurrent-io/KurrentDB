// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;
using KurrentDB.TestClient.Statistics;

namespace KurrentDB.TestClient.Commands;

internal class WriteFloodProcessor : ICmdProcessor {
	public string Usage {
		//                     0          1            2           3          4             5
		get { return "WRFL [<clients> <requests> [<streams-cnt> [<size> [<batchsize> [<stream-prefix>]]]]]"; }
	}

	public string Keyword {
		get { return "WRFL"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		long requestsCnt = 5000;
		int streamsCnt = 1000;
		int size = 256;
		int batchSize = 1;
		string streamNamePrefix = string.Empty;
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
					streamNamePrefix = args[5];
			} catch {
				return false;
			}
		}

		var stats = new WriteFloodStats(Keyword, context.OutputCsv, args);
		var monitor = new RequestMonitor();
		WriteFlood(context, stats, clientsCnt, requestsCnt, streamsCnt, size, batchSize, streamNamePrefix, monitor);
		return true;
	}

	private void WriteFlood(CommandProcessorContext context, WriteFloodStats stats, int clientsCnt, long requestsCnt, int streamsCnt,
		int size, int batchSize, string streamNamePrefix, RequestMonitor monitor) {
		context.IsAsync();

		var doneEvent = new ManualResetEventSlim(false);
		var clients = new List<TcpTypedConnection<byte[]>>();
		var threads = new List<Thread>();

		long last = 0;

		string[] streams = Enumerable.Range(0, streamsCnt).Select(x =>
			string.IsNullOrWhiteSpace(streamNamePrefix)
				? Guid.NewGuid().ToString()
				: $"{streamNamePrefix}-{x}"
		).ToArray();

		context.Log.Information("Writing streams randomly between {first} and {last}",
			streams.FirstOrDefault(),
			streams.LastOrDefault());

		Console.WriteLine($"Last stream: {streams.LastOrDefault()}");
		stats.StartTime = DateTime.UtcNow;
		var sw2 = new Stopwatch();
		for (int i = 0; i < clientsCnt; i++) {
			var count = requestsCnt / clientsCnt + ((i == clientsCnt - 1) ? requestsCnt % clientsCnt : 0);
			long sent = 0;
			long received = 0;
			var rnd = new Random();
			var client = context._tcpTestClient.CreateTcpConnection(
				context,
				(conn, pkg) => {
					if (pkg.Command != TcpCommand.WriteEventsCompleted) {
						context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
						return;
					}

					var dto = pkg.Data.Deserialize<WriteEventsCompleted>();
					monitor.EndOperation(pkg.CorrelationId);
					switch (dto.Result) {
						case OperationResult.Success:
							Interlocked.Add(ref stats.Succ, batchSize);
							if (stats.Succ - last > 1000) {
								last = stats.Succ;
								Console.Write(".");
							}

							break;
						case OperationResult.PrepareTimeout:
							Interlocked.Increment(ref stats.PrepTimeout);
							break;
						case OperationResult.CommitTimeout:
							Interlocked.Increment(ref stats.CommitTimeout);
							break;
						case OperationResult.ForwardTimeout:
							Interlocked.Increment(ref stats.ForwardTimeout);
							break;
						case OperationResult.WrongExpectedVersion:
							Interlocked.Increment(ref stats.WrongExpVersion);
							break;
						case OperationResult.StreamDeleted:
							Interlocked.Increment(ref stats.StreamDeleted);
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					if (dto.Result != OperationResult.Success)
						if (Interlocked.Increment(ref stats.Fail) % 1000 == 0)
							Console.Write('#');
					Interlocked.Increment(ref received);
					var localAll = Interlocked.Add(ref stats.All, batchSize);
					if (localAll % 100000 == 0) {
						stats.Elapsed = sw2.Elapsed;
						stats.Rate =  1000.0 * 100000 / stats.Elapsed.TotalMilliseconds;
						sw2.Restart();
						context.Log.Debug(
							"\nDONE TOTAL {writes} WRITES IN {elapsed} ({rate:0.0}/s) [S:{success}, F:{failures} (WEV:{wrongExpectedVersion}, " +
							"P:{prepareTimeout}, C:{commitTimeout}, F:{forwardTimeout}, D:{streamDeleted})].",
							localAll, stats.Elapsed, stats.Rate, stats.Succ, stats.Fail,
							stats.WrongExpVersion, stats.PrepTimeout, stats.CommitTimeout, stats.ForwardTimeout, stats.StreamDeleted);
						stats.WriteStatsToFile(context.StatsLogger);
					}

					if (localAll >= requestsCnt) {
						context.Success();
						doneEvent.Set();
					}
				},
				connectionClosed: (conn, err) => context.Fail(reason: "Connection was closed prematurely."));
			clients.Add(client);

			threads.Add(new Thread(() => {
				for (int j = 0; j < count; ++j) {
					var events = new NewEvent[batchSize];
					for (int q = 0; q < batchSize; q++) {
						events[q] = new NewEvent(Guid.NewGuid().ToByteArray(),
							"TakeSomeSpaceEvent",
							1, 0,
							EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(
								"{ \"DATA\" : \"" + new string('*', size) + "\"}"),
							EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(
								"{ \"METADATA\" : \"" + new string('$', 100) + "\"}"));
					}

					var corrid = Guid.NewGuid();
					var write = new WriteEvents(
						streams[rnd.Next(streamsCnt)],
						ExpectedVersion.Any,
						events,
						false);
					var package = new TcpPackage(TcpCommand.WriteEvents, corrid, write.Serialize());
					monitor.StartOperation(corrid);
					client.EnqueueSend(package.AsByteArray());

					var localSent = Interlocked.Increment(ref sent);
					while (localSent - Interlocked.Read(ref received) >
					       context._tcpTestClient.Options.WriteWindow / clientsCnt) {
						Thread.Sleep(1);
					}
				}
			}) {IsBackground = true});
		}

		var sw = Stopwatch.StartNew();
		sw2.Start();
		threads.ForEach(thread => thread.Start());
		doneEvent.Wait();
		sw.Stop();
		clients.ForEach(client => client.Close());

		stats.WriteStatsToFile(context.StatsLogger);
		context.Log.Information(
			"Completed. Successes: {success}, failures: {failures} (WRONG VERSION: {wrongExpectedVersion}, P: {prepareTimeout}, " +
			"C: {commitTimeout}, F: {forwardTimeout}, D: {streamDeleted})",
			stats.Succ, stats.Fail, stats.WrongExpVersion, stats.PrepTimeout, stats.CommitTimeout, stats.ForwardTimeout, stats.StreamDeleted);

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
			string.Format("{0}-c{1}-r{2}-st{3}-s{4}-reqPerSec", Keyword, clientsCnt, requestsCnt, streamsCnt, size),
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
