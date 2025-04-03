// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using EventStore.Common.Utils;

namespace KurrentDB.TestClient.Commands;

internal class SubscriptionStressTestProcessor : ICmdProcessor {
	public string Usage {
		get { return "SST [<subscription-count>]"; }
	}

	public string Keyword {
		get { return "SST"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		int subscriptionCount = 5000;

		if (args.Length > 0) {
			if (args.Length > 1)
				return false;
			subscriptionCount = MetricPrefixValue.ParseInt(args[0]);
		}

		context.IsAsync();

		var conn = EventStoreConnection.Create(ConnectionSettings.Create()
				.UseCustomLogger(new ClientApiLoggerBridge(context.Log))
				.FailOnNoServerResponse()
			/*.EnableVerboseLogging()*/,
			new Uri($"tcp://{context._tcpTestClient.TcpEndpoint.GetHost()}:{context._tcpTestClient.TcpEndpoint.GetPort()}"));
		conn.ConnectAsync().Wait();

		long appearedCnt = 0;
		var sw = Stopwatch.StartNew();
		for (int i = 0; i < subscriptionCount; ++i) {
			conn.SubscribeToStreamAsync(
				string.Format("stream-{0}", i),
				false,
				(s, e) => {
					var c = Interlocked.Increment(ref appearedCnt);
					if (c % 1000 == 0) Console.Write('\'');
					if (c % 100000 == 0) {
						context.Log.Debug("Received total {events} events ({rate} per sec)...", c,
							100000.0 / sw.Elapsed.TotalSeconds);
						sw.Restart();
					}

					return Task.CompletedTask;
				}).Wait();
		}

		context.Log.Information("Subscribed to {subscriptionCount} streams...", subscriptionCount);
		return true;
	}
}
