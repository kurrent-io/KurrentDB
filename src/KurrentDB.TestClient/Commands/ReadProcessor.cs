// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class ReadProcessor : ICmdProcessor {
	public string Usage {
		get { return "RD [<stream-id> [<from-number> [<only-if-leader>]]]"; }
	}

	public string Keyword {
		get { return "RD"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		var eventStreamId = "test-stream";
		var fromNumber = 0;
		const bool resolveLinkTos = false;
		var requireLeader = false;

		if (args.Length > 0) {
			if (args.Length > 3)
				return false;
			eventStreamId = args[0];
			if (args.Length >= 2)
				fromNumber = MetricPrefixValue.ParseInt(args[1]);
			if (args.Length >= 3)
				requireLeader = bool.Parse(args[2]);
		}

		context.IsAsync();

		var sw = new Stopwatch();
		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information("[{remoteEndPoint}, L{localEndPoint}]: Reading...", conn.RemoteEndPoint,
					conn.LocalEndPoint);
				var readDto =
					new ReadEvent(eventStreamId, fromNumber, resolveLinkTos, requireLeader);
				var package =
					new TcpPackage(TcpCommand.ReadEvent, Guid.NewGuid(), readDto.Serialize()).AsByteArray();
				sw.Start();
				conn.EnqueueSend(package);
			},
			handlePackage: (conn, pkg) => {
				sw.Stop();
				context.Log.Information("Read request took: {elapsed}.", sw.Elapsed);

				if (pkg.Command != TcpCommand.ReadEventCompleted) {
					context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
					return;
				}

				var dto = pkg.Data.Deserialize<ReadEventCompleted>();
				context.Log.Information("READ events from <{stream}>:\n\n"
				                 + "\tEventStreamId: {stream}\n"
				                 + "\tEventNumber:   {eventNumber}\n"
				                 + "\tReadResult:    {readResult}\n"
				                 + "\tEventType:     {eventType}\n"
				                 + "\tData:          {data}\n"
				                 + "\tMetadata:      {metadata}\n",
					eventStreamId,
					eventStreamId,
					dto.Event.Event.EventNumber,
					dto.Result,
					dto.Event.Event.EventType,
					Helper.UTF8NoBom.GetString(dto.Event.Event.Data.ToByteArray()),
					Helper.UTF8NoBom.GetString(dto.Event.Event.Metadata.ToByteArray()));


				if (dto.Result == ReadEventCompleted.Types.ReadEventResult.Success) {
					PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
						(int)Math.Round(sw.Elapsed.TotalMilliseconds));
					context.Success();
				} else
					context.Fail();

				conn.Close();
			},
			connectionClosed: (connection, error) => context.Fail(reason: "Connection was closed prematurely."));

		return true;
	}
}
