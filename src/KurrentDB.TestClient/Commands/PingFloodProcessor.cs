// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class PingFloodProcessor : ICmdProcessor {
	private static readonly byte[] Payload = new byte[0];

	public string Usage {
		get { return "PINGFL [<clients> <messages>]"; }
	}
	
	public string Keyword {
		get { return "PINGFL"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		long requestsCnt = 1000000;
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

		PingFlood(context, clientsCnt, requestsCnt);
		return true;
	}

	private void PingFlood(CommandProcessorContext context, int clientsCnt, long requestsCnt) {
		context.IsAsync();

		var doneEvent = new ManualResetEventSlim(false);
		var clients = new List<TcpTypedConnection<byte[]>>();
		var threads = new List<Thread>();
		long all = 0;

		for (int i = 0; i < clientsCnt; i++) {
			var count = requestsCnt / clientsCnt + ((i == clientsCnt - 1) ? requestsCnt % clientsCnt : 0);
			long received = 0;
			long sent = 0;
			var client = context._tcpTestClient.CreateTcpConnection(
				context,
				(conn, msg) => {
					Interlocked.Increment(ref received);
					var pongs = Interlocked.Increment(ref all);
					if (pongs % 10000 == 0) Console.Write('.');
					if (pongs == requestsCnt) {
						context.Success();
						doneEvent.Set();
					}
				},
				connectionClosed: (conn, err) => context.Fail(reason: "Connection was closed prematurely."));
			clients.Add(client);

			threads.Add(new Thread(() => {
				for (int j = 0; j < count; ++j) {
					var package = new TcpPackage(TcpCommand.Ping, Guid.NewGuid(), Payload);
					client.EnqueueSend(package.AsByteArray());

					var localSent = Interlocked.Increment(ref sent);
					while (localSent - Interlocked.Read(ref received) >
					       context._tcpTestClient.Options.PingWindow / clientsCnt) {
						Thread.Sleep(1);
					}
				}
			}) {IsBackground = true});
		}

		var sw = Stopwatch.StartNew();
		threads.ForEach(thread => thread.Start());
		doneEvent.Wait();
		sw.Stop();
		clients.ForEach(client => client.Close());

		var reqPerSec = (all + 0.0) / sw.ElapsedMilliseconds * 1000;
		context.Log.Information("{requests} requests completed in {elapsed}ms ({rate:0.00} reqs per sec).", all,
			sw.ElapsedMilliseconds, reqPerSec);
		PerfUtils.LogData(Keyword,
			PerfUtils.Row(PerfUtils.Col("clientsCnt", clientsCnt),
				PerfUtils.Col("requestsCnt", requestsCnt),
				PerfUtils.Col("ElapsedMilliseconds", sw.ElapsedMilliseconds)));
		PerfUtils.LogTeamCityGraphData(string.Format("{0}-{1}-{2}-reqPerSec", Keyword, clientsCnt, requestsCnt),
			(int)reqPerSec);

		if (Interlocked.Read(ref all) == requestsCnt)
			context.Success();
		else
			context.Fail();
	}
}
