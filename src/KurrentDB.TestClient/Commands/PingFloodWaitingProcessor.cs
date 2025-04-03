// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class PingFloodWaitingProcessor : ICmdProcessor {
	public string Usage {
		get { return "PINGFLW [<clients> <messages>]"; }
	}

	public string Keyword {
		get { return "PINGFLW"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		long requestsCnt = 100000;
		if (args.Length > 0) {
			if (args.Length != 2)
				return false;
			try {
				clientsCnt = MetricPrefixValue.ParseInt(args[0]);
				requestsCnt = MetricPrefixValue.ParseLong(args[1]);
			} catch {
				return false;
			}
		}

		PingFloodWaiting(context, clientsCnt, requestsCnt);
		return true;
	}

	private void PingFloodWaiting(CommandProcessorContext context, int clientsCnt, long requestsCnt) {
		context.IsAsync();

		var clients = new List<TcpTypedConnection<byte[]>>();
		var threads = new List<Thread>();
		var doneEvent = new ManualResetEventSlim(false);
		var clientsDone = 0;
		long all = 0;
		for (int i = 0; i < clientsCnt; i++) {
			var autoResetEvent = new AutoResetEvent(false);
			var client = context._tcpTestClient.CreateTcpConnection(
				context,
				(_, __) => {
					Interlocked.Increment(ref all);
					autoResetEvent.Set();
				},
				connectionClosed: (conn, err) => context.Fail(reason: "Connection was closed prematurely."));
			clients.Add(client);

			var count = requestsCnt / clientsCnt + ((i == clientsCnt - 1) ? requestsCnt % clientsCnt : 0);
			threads.Add(new Thread(() => {
				for (int j = 0; j < count; ++j) {
					var package = new TcpPackage(TcpCommand.Ping, Guid.NewGuid(), null);
					client.EnqueueSend(package.AsByteArray());
					autoResetEvent.WaitOne();
				}

				if (Interlocked.Increment(ref clientsDone) == clientsCnt) {
					context.Success();
					doneEvent.Set();
				}
			}) {IsBackground = true});
		}

		var sw = Stopwatch.StartNew();
		threads.ForEach(thread => thread.Start());
		doneEvent.Wait();
		sw.Stop();
		clients.ForEach(x => x.Close());

		var reqPerSec = (all + 0.0) / sw.ElapsedMilliseconds * 1000;
		context.Log.Information("{requests} requests completed in {elapsed}ms ({rate:0.00} reqs per sec).", all,
			sw.ElapsedMilliseconds, reqPerSec);
		PerfUtils.LogData(Keyword,
			PerfUtils.Row(PerfUtils.Col("clientsCnt", clientsCnt),
				PerfUtils.Col("requestsCnt", requestsCnt),
				PerfUtils.Col("ElapsedMilliseconds", sw.ElapsedMilliseconds)));
		PerfUtils.LogTeamCityGraphData(string.Format("{0}-{1}-{2}-reqPerSec", Keyword, clientsCnt, requestsCnt),
			(int)reqPerSec);
		PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
			(int)Math.Round(sw.Elapsed.TotalMilliseconds / all));

		if (Interlocked.Read(ref all) == requestsCnt)
			context.Success();
		else
			context.Fail();
	}
}
