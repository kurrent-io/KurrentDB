// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class ReadFloodProcessor : ICmdProcessor {
	public string Usage {
		//                     0          1           2               3                  4
		get { return "RDFL [<clients> <requests> [<streams-cnt> [<stream-prefix> [<require-leader>]]]]"; }
	}
	
	public string Keyword {
		get { return "RDFL"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int clientsCnt = 1;
		long requestsCnt = 5000;
		int streamsCnt = 1;
		var streamPrefix = "test-stream";
		bool resolveLinkTos = false;
		bool requireLeader = false;
		if (args.Length > 0) {
			if (args.Length != 2 && args.Length != 3 && args.Length != 4 && args.Length != 5)
				return false;

			try {
				clientsCnt = MetricPrefixValue.ParseInt(args[0]);
				requestsCnt = MetricPrefixValue.ParseLong(args[1]);
				if (args.Length >= 3)
					streamsCnt = MetricPrefixValue.ParseInt(args[2]);
				if (args.Length >= 4)
					streamPrefix = args[3];
				if (args.Length >= 5)
					requireLeader = bool.Parse(args[4]);
			} catch {
				return false;
			}
		}

		RequestMonitor monitor = new RequestMonitor();
		ReadFlood(context, streamPrefix, clientsCnt, requestsCnt, streamsCnt, resolveLinkTos, requireLeader, monitor);
		return true;
	}

	private void ReadFlood(CommandProcessorContext context, string streamPrefix, int clientsCnt, long requestsCnt, int streamsCnt,
		bool resolveLinkTos, bool requireLeader, RequestMonitor monitor) {
		context.IsAsync();

		string[] streams = streamsCnt == 1
			? new[] { streamPrefix }
			: Enumerable.Range(0, streamsCnt).Select(x => $"{streamPrefix}-{x}").ToArray();

		context.Log.Information("Reading streams {first} through to {last}",
			streams.FirstOrDefault(),
			streams.LastOrDefault());

		var clients = new List<TcpTypedConnection<byte[]>>();
		var threads = new List<Thread>();
		var doneEvent = new ManualResetEventSlim(false);
		var sw2 = new Stopwatch();
		long succ = 0;
		long fail = 0;
		long all = 0;
		for (int i = 0; i < clientsCnt; i++) {
			var count = requestsCnt / clientsCnt + ((i == clientsCnt - 1) ? requestsCnt % clientsCnt : 0);
			long received = 0;
			long sent = 0;
			var client = context._tcpTestClient.CreateTcpConnection(
				context,
				(conn, pkg) => {
					if (pkg.Command != TcpCommand.ReadEventCompleted) {
						context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
						return;
					}

					var dto = pkg.Data.Deserialize<ReadEventCompleted>();
					monitor.EndOperation(pkg.CorrelationId);
					if (dto.Result == ReadEventCompleted.Types.ReadEventResult.Success) {
						if (Interlocked.Increment(ref succ) % 1000 == 0) Console.Write(".");
					} else {
						if (Interlocked.Increment(ref fail) % 1000 == 0) Console.Write("#");
					}
					
					Interlocked.Increment(ref received);
					var localAll = Interlocked.Increment(ref all);
					if (localAll % 100000 == 0) {
						var elapsed = sw2.Elapsed;
						sw2.Restart();
						context.Log.Information("\nDONE TOTAL {reads} READS IN {elapsed} ({rate:0.0}/s).", localAll,
							elapsed, 1000.0 * 100000 / elapsed.TotalMilliseconds);
					}

					if (localAll == requestsCnt) {
						doneEvent.Set();
					}
				},
				connectionClosed: (conn, err) => context.Fail(reason: "Connection was closed prematurely."));
			clients.Add(client);

			var clientNum = i;
			threads.Add(new Thread(() => {
				int streamIndex = (streamsCnt / clientsCnt) * clientNum;
				context.Log.Information("Reader #{clientNum} performing {count} reads on {streamsCnt} streams starting at stream index {streamIndex}",
					clientNum, count, streamsCnt, streamIndex);
				for (int j = 0; j < count; ++j) {
					var corrId = Guid.NewGuid();

					var eventStreamId = streams[streamIndex++];
					if (streamIndex >= streamsCnt)
						streamIndex = 0;
					var read = new ReadEvent(eventStreamId, 0, resolveLinkTos, requireLeader);
					var package = new TcpPackage(TcpCommand.ReadEvent, corrId, read.Serialize());
					monitor.StartOperation(corrId);
					client.EnqueueSend(package.AsByteArray());

					var localSent = Interlocked.Increment(ref sent);
					while (localSent - Interlocked.Read(ref received) >
					       context._tcpTestClient.Options.ReadWindow / clientsCnt) {
						Thread.Sleep(1);
					}
				}
				context.Log.Information("Reader #{clientNum} done", clientNum);
				
			}) {IsBackground = true});
		}

		var sw = Stopwatch.StartNew();
		sw2.Start();
		threads.ForEach(thread => thread.Start());
		doneEvent.Wait();
		sw.Stop();
		clients.ForEach(client => client.Close());

		var reqPerSec = (all + 0.0) / sw.ElapsedMilliseconds * 1000;
		context.Log.Information("Completed. READS succ: {success}, fail: {failures}.", Interlocked.Read(ref succ),
			Interlocked.Read(ref fail));
		context.Log.Information("{requests} requests completed in {elapsed}ms ({rate:0.00} reqs per sec).", all,
			sw.ElapsedMilliseconds, reqPerSec);
		monitor.GetMeasurementDetails();
		PerfUtils.LogData(Keyword,
			PerfUtils.Row(PerfUtils.Col("clientsCnt", clientsCnt),
				PerfUtils.Col("requestsCnt", requestsCnt),
				PerfUtils.Col("ElapsedMilliseconds", sw.ElapsedMilliseconds)),
			PerfUtils.Row(PerfUtils.Col("readsCnt", all)));
		PerfUtils.LogTeamCityGraphData(string.Format("{0}-{1}-{2}-reqPerSec", Keyword, clientsCnt, requestsCnt),
			(int)reqPerSec);

		if (succ != requestsCnt)
			context.Fail(reason: "There were errors or not all requests completed.");
		else
			context.Success();
	}
}
