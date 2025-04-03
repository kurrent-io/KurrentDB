// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Client.Messages;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using OperationResult = EventStore.Client.Messages.OperationResult;

namespace KurrentDB.TestClient.Commands;

internal class TransactionWriteProcessor : ICmdProcessor {
	public string Usage {
		get { return "TWR [<stream-id> [<expected-version> [<events-cnt>]]]"; }
	}

	public string Keyword {
		get { return "TWR"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		var eventStreamId = "test-stream";
		var expectedVersion = ExpectedVersion.Any;
		int eventsCnt = 10;

		if (args.Length > 0) {
			if (args.Length > 3)
				return false;
			eventStreamId = args[0];
			if (args.Length > 1)
				expectedVersion = args[1].ToUpper() == "ANY" ? ExpectedVersion.Any : int.Parse(args[1]);
			if (args.Length > 2)
				eventsCnt = MetricPrefixValue.ParseInt(args[1]);
		}

		context.IsAsync();

		var sw = new Stopwatch();
		var stage = Stage.AcquiringTransactionId;
		long transactionId = -1;
		var writtenEvents = 0;
		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information("[{remoteEndPoint}, L{localEndPoint}]: Starting transaction...",
					conn.RemoteEndPoint, conn.LocalEndPoint);
				sw.Start();

				var tranStart = new TransactionStart(eventStreamId, expectedVersion, false);
				var package = new TcpPackage(TcpCommand.TransactionStart, Guid.NewGuid(), tranStart.Serialize());
				conn.EnqueueSend(package.AsByteArray());
			},
			handlePackage: (conn, pkg) => {
				switch (stage) {
					case Stage.AcquiringTransactionId: {
						if (pkg.Command != TcpCommand.TransactionStartCompleted) {
							context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
							return;
						}

						var dto = pkg.Data.Deserialize<TransactionStartCompleted>();
						if (dto.Result != OperationResult.Success) {
							var msg = string.Format("Error while starting transaction: {0} ({1}).", dto.Message,
								dto.Result);
							context.Log.Information("Error while starting transaction: {message} ({e}).", dto.Message,
								dto.Result);
							context.Fail(reason: msg);
						} else {
							context.Log.Information("Successfully started transaction. TransactionId: {transactionId}.",
								dto.TransactionId);
							context.Log.Information("Now sending transactional events. TransactionId: {transactionId}",
								dto.TransactionId);

							transactionId = dto.TransactionId;
							stage = Stage.Writing;
							for (int i = 0; i < eventsCnt; ++i) {
								var writeDto = new TransactionWrite(
									transactionId,
									new[] {
										new NewEvent(Guid.NewGuid().ToByteArray(),
											"TakeSomeSpaceEvent",
											0, 0,
											EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(Guid.NewGuid().ToString()),
											EventStore.Common.Utils.Helper.UTF8NoBom.GetBytes(Guid.NewGuid().ToString()))
									},
									false);
								var package = new TcpPackage(TcpCommand.TransactionWrite, Guid.NewGuid(),
									writeDto.Serialize());
								conn.EnqueueSend(package.AsByteArray());
							}
						}

						break;
					}
					case Stage.Writing: {
						if (pkg.Command != TcpCommand.TransactionWriteCompleted) {
							context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
							return;
						}

						var dto = pkg.Data.Deserialize<TransactionWriteCompleted>();
						if (dto.Result != OperationResult.Success) {
							context.Log.Information("Error while writing transactional event: {message} ({e}).",
								dto.Message, dto.Result);
							var msg = String.Format("Error while writing transactional event: {0} ({1}).",
								dto.Message, dto.Result);
							context.Fail(reason: msg);
						} else {
							writtenEvents += 1;
							if (writtenEvents == eventsCnt) {
								context.Log.Information("Written all events. Committing...");

								stage = Stage.Committing;
								var commitDto = new TransactionCommit(transactionId, false);
								var package = new TcpPackage(TcpCommand.TransactionCommit, Guid.NewGuid(),
									commitDto.Serialize());
								conn.EnqueueSend(package.AsByteArray());
							}
						}

						break;
					}
					case Stage.Committing: {
						if (pkg.Command != TcpCommand.TransactionCommitCompleted) {
							context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
							return;
						}

						sw.Stop();

						var dto = pkg.Data.Deserialize<TransactionCommitCompleted>();
						if (dto.Result != OperationResult.Success) {
							var msg = string.Format("Error while committing transaction: {0} ({1}).", dto.Message,
								dto.Result);
							context.Log.Information("Error while committing transaction: {message} ({e}).", dto.Message,
								dto.Result);
							context.Log.Information("Transaction took: {elapsed}.", sw.Elapsed);
							context.Fail(reason: msg);
						} else {
							context.Log.Information("Successfully committed transaction [{transactionId}]!",
								dto.TransactionId);
							context.Log.Information("Transaction took: {elapsed}.", sw.Elapsed);
							PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
								(int)Math.Round(sw.Elapsed.TotalMilliseconds));
							context.Success();
						}

						conn.Close();
						break;
					}
					default:
						throw new ArgumentOutOfRangeException();
				}
			},
			connectionClosed: (connection, error) => context.Fail(reason: "Connection was closed prematurely."));
		context.WaitForCompletion();
		return true;
	}

	private enum Stage {
		AcquiringTransactionId,
		Writing,
		Committing
	}
}
