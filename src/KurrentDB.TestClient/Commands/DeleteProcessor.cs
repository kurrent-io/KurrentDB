// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Client.Messages;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using OperationResult = EventStore.Client.Messages.OperationResult;

namespace KurrentDB.TestClient.Commands;

internal class DeleteProcessor : ICmdProcessor {
	public string Usage {
		get { return "DEL [<stream-id> [<expected-version>]]"; }
	}

	public string Keyword {
		get { return "DEL"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		var eventStreamId = "test-stream";
		var expectedVersion = ExpectedVersion.Any;

		if (args.Length > 0) {
			if (args.Length > 2)
				return false;
			eventStreamId = args[0];
			if (args.Length == 2)
				expectedVersion = args[1].Trim().ToUpper() == "ANY" ? ExpectedVersion.Any : int.Parse(args[1]);
		}

		context.IsAsync();
		var sw = new Stopwatch();
		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information(
					"[{remoteEndPoint}, L{localEndPoint}]: Trying to delete event stream '{stream}'...",
					conn.RemoteEndPoint, conn.LocalEndPoint, eventStreamId);
				var corrid = Guid.NewGuid();
				var deleteDto = new DeleteStream(eventStreamId, expectedVersion, false, true);
				var package = new TcpPackage(TcpCommand.DeleteStream, corrid, deleteDto.Serialize()).AsByteArray();
				sw.Start();
				conn.EnqueueSend(package);
			},
			handlePackage: (conn, pkg) => {
				sw.Stop();
				context.Log.Information("Delete request took: {elapsed}.", sw.Elapsed);

				if (pkg.Command != TcpCommand.DeleteStreamCompleted) {
					context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
					return;
				}

				var dto = pkg.Data.Deserialize<DeleteStreamCompleted>();
				if (dto.Result == OperationResult.Success) {
					context.Log.Information("DELETED event stream {stream}.", eventStreamId);
					PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
						(int)Math.Round(sw.Elapsed.TotalMilliseconds));
					context.Success();
				} else {
					context.Log.Information("DELETION FAILED for event stream {stream}: {message} ({e}).", eventStreamId,
						dto.Message, dto.Result);
					context.Fail();
				}

				conn.Close();
			},
			connectionClosed: (connection, error) => context.Fail(reason: "Connection was closed prematurely."));

		context.WaitForCompletion();
		return true;
	}
}
