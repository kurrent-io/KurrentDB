// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class WriteLongTermProcessor : ICmdProcessor {
	public string Usage {
		get {
			return
				"WRLT <clients> <min req. per second> <max req. per second> <run for n minutes> [<event-stream>]";
		}
	}

	public string Keyword {
		get { return "WRLT"; }
	}

	private readonly object _randomLockRoot = new object();
	private readonly Random _random = new Random();

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		int minPerSecond = 1;
		int maxPerSecond = 2;
		int runTimeMinutes = 1;
		string eventStreamId = null;

		if (args.Length > 0) {
			if (args.Length != 4 && args.Length != 5)
				return false;

			try {
				clientsCnt = MetricPrefixValue.ParseInt(args[0]);
				minPerSecond = MetricPrefixValue.ParseInt(args[1]);
				maxPerSecond = MetricPrefixValue.ParseInt(args[2]);
				runTimeMinutes = MetricPrefixValue.ParseInt(args[3]);
				if (args.Length == 5)
					eventStreamId = args[4];
			} catch {
				return false;
			}
		}

		Flood(context, eventStreamId, clientsCnt, minPerSecond, maxPerSecond, runTimeMinutes);
		return true;
	}

	private void Flood(CommandProcessorContext context,
		string eventStreamId,
		int clientsCnt,
		int minPerSecond,
		int maxPerSecond,
		int runTimeMinutes) {
		context.IsAsync();

		var clients = new List<TcpTypedConnection<byte[]>>();
		var threads = new List<Thread>();
		var doneEvent = new ManualResetEvent(false);
		var done = false;

		var succ = 0;
		var fail = 0;

		var requestsCnt = 0;

		int sent = 0;
		int received = 0;

		var watchLockRoot = new object();
		var sw = Stopwatch.StartNew();
		for (int i = 0; i < clientsCnt; i++) {
			var esId = eventStreamId ?? "Stream-" + Thread.CurrentThread.ManagedThreadId % 3;

			var client = context._tcpTestClient.CreateTcpConnection(
				context,
				(conn, pkg) => {
					if (pkg.Command != TcpCommand.WriteEventsCompleted) {
						context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
						return;
					}

					var dto = pkg.Data.Deserialize<WriteEventsCompleted>();
					if (dto.Result == OperationResult.Success) {
						var succDone = Interlocked.Increment(ref succ);
						if (succDone % maxPerSecond == 0)
							Console.Write(".");

						Interlocked.Increment(ref requestsCnt);
					} else
						Interlocked.Increment(ref fail);

					Interlocked.Increment(ref received);
				},
				connectionClosed: (conn, err) => {
					if (!done)
						context.Fail(reason: "Socket was closed, but not all requests were completed.");
					else
						context.Success();
				});
			clients.Add(client);

			threads.Add(new Thread(() => {
				var sentCount = 0;
				var sleepTime = 0;

				var dataSizeCoefficient = 1;
				var currentMinute = -1;

				while (true) {
					TimeSpan elapsed;
					lock (watchLockRoot)
						elapsed = sw.Elapsed;

					if (elapsed.TotalMinutes > runTimeMinutes) {
						done = true;
						doneEvent.Set();
						break;
					}

					if (sentCount == 0) {
						int elapsedMinutesInt = (int)elapsed.TotalMinutes;
						lock (_randomLockRoot) {
							sentCount = minPerSecond == maxPerSecond
								? maxPerSecond
								: _random.Next(minPerSecond, maxPerSecond);
							dataSizeCoefficient = _random.Next(8, 256);
						}

						if (currentMinute != elapsedMinutesInt) {
							currentMinute = elapsedMinutesInt;
							context.Log.Information(
								"\nElapsed {elapsed} of {runTime} minutes, sent {sent}; next block coef. {dataSizeCoefficient}",
								elapsedMinutesInt,
								runTimeMinutes,
								sent,
								dataSizeCoefficient);
						}

						sleepTime = 1000 / sentCount;
					}

					var dataSize = dataSizeCoefficient * 8;
					var write = new WriteEvents(
						esId,
						ExpectedVersion.Any,
						new[] {
							new NewEvent(
								Guid.NewGuid().ToByteArray(),
								"TakeSomeSpaceEvent",
								0, 0,
								Helper.UTF8NoBom.GetBytes(
									"DATA" + dataSize.ToString(" 00000 ") + new string('*', dataSize)),
								Helper.UTF8NoBom.GetBytes("METADATA" + new string('$', 100)))
						},
						false);
					var package = new TcpPackage(TcpCommand.WriteEvents, Guid.NewGuid(), write.Serialize());
					client.EnqueueSend(package.AsByteArray());

					Interlocked.Increment(ref sent);

					Thread.Sleep(sleepTime);
					sentCount -= 1;

					while (sent - received > context._tcpTestClient.Options.WriteWindow / clientsCnt) {
						Thread.Sleep(1);
					}
				}
			}));
		}

		foreach (var thread in threads) {
			thread.IsBackground = true;
			thread.Start();
		}

		doneEvent.WaitOne();
		sw.Stop();

		foreach (var client in clients) {
			client.Close();
		}

		context.Log.Information("Completed. Successes: {success}, failures: {failures}", succ, fail);
		var reqPerSec = (requestsCnt + 0.0) / sw.ElapsedMilliseconds * 1000;
		context.Log.Information("{requests} requests completed in {elapsed}ms ({rate:0.00} reqs per sec).",
			requestsCnt,
			sw.ElapsedMilliseconds,
			reqPerSec);

		PerfUtils.LogData(
			Keyword,
			PerfUtils.Row(PerfUtils.Col("clientsCnt", clientsCnt),
				PerfUtils.Col("requestsCnt", requestsCnt),
				PerfUtils.Col("ElapsedMilliseconds", sw.ElapsedMilliseconds)),
			PerfUtils.Row(PerfUtils.Col("successes", succ), PerfUtils.Col("failures", fail))
		);

		PerfUtils.LogTeamCityGraphData(string.Format("{0}-{1}-{2}-reqPerSec", Keyword, clientsCnt, requestsCnt),
			(int)reqPerSec);

		PerfUtils.LogTeamCityGraphData(
			string.Format("{0}-{1}-{2}-failureSuccessRate", Keyword, clientsCnt, requestsCnt),
			100 * fail / (fail + succ));

		context.Success();
	}
}
