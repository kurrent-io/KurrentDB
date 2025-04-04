// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Services.Transport.Tcp;

namespace KurrentDB.TestClient.Commands;

internal class ReadAllProcessor : ICmdProcessor {
	public string Usage {
		get { return "RDALL [[F|B] [<commit pos> <prepare pos> [<only-if-leader>]]]"; }
	}

	public string Keyword {
		get { return "RDALL"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		bool forward = true;
		long commitPos = 0;
		long preparePos = 0;
		bool posOverriden = false;
		bool resolveLinkTos = false;
		bool requireLeader = false;

		if (args.Length > 0) {
			if (args.Length != 1 && args.Length != 3 && args.Length != 4)
				return false;

			if (args[0].ToUpper() == "F")
				forward = true;
			else if (args[0].ToUpper() == "B")
				forward = false;
			else
				return false;

			if (args.Length >= 3) {
				posOverriden = true;
				if (!long.TryParse(args[1], out commitPos) || !long.TryParse(args[2], out preparePos))
					return false;
			}

			if (args.Length >= 4)
				requireLeader = bool.Parse(args[3]);
		}

		if (!posOverriden) {
			commitPos = forward ? 0 : -1;
			preparePos = forward ? 0 : -1;
		}

		context.IsAsync();

		int total = 0;
		var sw = new Stopwatch();
		var tcpCommand = forward ? TcpCommand.ReadAllEventsForward : TcpCommand.ReadAllEventsBackward;
		var tcpCommandToReceive = forward ? TcpCommand.ReadAllEventsForwardCompleted : TcpCommand.ReadAllEventsBackwardCompleted;

		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information("[{remoteEndPoint}, L{localEndPoint}]: Reading all {readDirection}...",
					conn.RemoteEndPoint, conn.LocalEndPoint, forward ? "FORWARD" : "BACKWARD");

				var readDto =
					new ReadAllEvents(commitPos, preparePos, 10, resolveLinkTos, requireLeader);
				var package = new TcpPackage(tcpCommand, Guid.NewGuid(), readDto.Serialize()).AsByteArray();
				sw.Start();
				conn.EnqueueSend(package);
			},
			handlePackage: (conn, pkg) => {
				if (pkg.Command != tcpCommandToReceive) {
					context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
					return;
				}

				var dto = pkg.Data.Deserialize<ReadAllEventsCompleted>();
				if (dto.Events.IsEmpty()) {
					sw.Stop();
					context.Log.Information("=== Reading ALL {readDirection} completed in {elapsed}. Total read: {total}",
						forward ? "FORWARD" : "BACKWARD", sw.Elapsed, total);
					context.Success();
					conn.Close();
					return;
				}

				total += dto.Events.Count;

				var readDto = new ReadAllEvents(dto.NextCommitPosition, dto.NextPreparePosition,
					10, resolveLinkTos, requireLeader);
				var package = new TcpPackage(tcpCommand, Guid.NewGuid(), readDto.Serialize()).AsByteArray();
				conn.EnqueueSend(package);
			},
			connectionClosed: (connection, error) => context.Fail(reason: "Connection was closed prematurely."));

		context.WaitForCompletion();
		return true;
	}
}
