// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Linq;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class MultiWriteProcessor : ICmdProcessor {
	public string Usage {
		get { return "MWR [<write-count=10> [<stream=test-stream> [<expected-version=ANY>]]"; }
	}
	
	public string Keyword {
		get { return "MWR"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		const string data = "test-data";
		var eventStreamId = "test-stream";
		var writeCount = 10;
		var expectedVersion = ExpectedVersion.Any;

		if (args.Length > 0) {
			if (args.Length > 3)
				return false;
			writeCount = MetricPrefixValue.ParseInt(args[0]);
			if (args.Length >= 2)
				eventStreamId = args[1];
			if (args.Length >= 3)
				expectedVersion = args[2].Trim().ToUpper() == "ANY"
					? ExpectedVersion.Any
					: int.Parse(args[2].Trim());
		}

		context.IsAsync();
		var sw = new Stopwatch();
		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information("[{remoteEndPoint}, L{localEndPoint}]: Writing...", conn.RemoteEndPoint,
					conn.LocalEndPoint);
				var writeDto = new WriteEvents(
					eventStreamId,
					expectedVersion,
					Enumerable.Range(0, writeCount).Select(x => new NewEvent(
						Guid.NewGuid().ToByteArray(),
						"type",
						0, 0,
						Helper.UTF8NoBom.GetBytes(data),
						new byte[0])).ToArray(),
					false);
				var package = new TcpPackage(TcpCommand.WriteEvents, Guid.NewGuid(), writeDto.Serialize())
					.AsByteArray();
				sw.Start();
				conn.EnqueueSend(package);
			},
			handlePackage: (conn, pkg) => {
				sw.Stop();
				context.Log.Information("Write request took: {elapsed}.", sw.Elapsed);

				if (pkg.Command != TcpCommand.WriteEventsCompleted) {
					context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
					return;
				}

				var dto = pkg.Data.Deserialize<WriteEventsCompleted>();
				if (dto.Result == EventStore.Client.Messages.OperationResult.Success) {
					context.Log.Information("Successfully written {writeCount} events.", writeCount);
					PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
						(int)Math.Round(sw.Elapsed.TotalMilliseconds));
					context.Success();
				} else {
					context.Log.Information("Error while writing: {e}.", dto.Result);
					context.Fail();
				}

				conn.Close();
			},
			connectionClosed: (connection, error) => context.Fail(reason: "Connection was closed prematurely."));

		context.WaitForCompletion();
		return true;
	}
}
