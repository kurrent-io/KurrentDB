// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.Net.Sockets;
using EventStore.Client.Messages;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Http.Codecs;

namespace KurrentDB.TestClient.Commands;

internal class WriteJsonProcessor : ICmdProcessor {
	public string Usage {
		get { return "WRJ [<stream-id> <expected-version> <data> [<metadata>]]"; }
	}

	public string Keyword {
		get { return "WRJ"; }
	}

	private readonly Random _random = new Random();

	public bool Execute(CommandProcessorContext context, string[] args) {
		var eventStreamId = "test-stream";
		var expectedVersion = ExpectedVersion.Any;
		var data = GenerateTestData();
		string metadata = null;

		if (args.Length > 0) {
			if (args.Length < 3 || args.Length > 4)
				return false;
			eventStreamId = args[0];
			expectedVersion = args[1].ToUpper() == "ANY" ? ExpectedVersion.Any : int.Parse(args[1]);
			data = args[2];
			if (args.Length == 4)
				metadata = args[3];
		}

		context.IsAsync();
		var writeDto = new WriteEvents(
			eventStreamId,
			expectedVersion,
			new[] {
				new NewEvent(Guid.NewGuid().ToByteArray(),
					"JsonDataEvent",
					1, 0,
					Helper.UTF8NoBom.GetBytes(data),
					Helper.UTF8NoBom.GetBytes(metadata ?? string.Empty))
			},
			false);
		var package = new TcpPackage(TcpCommand.WriteEvents, Guid.NewGuid(), writeDto.Serialize());

		var sw = new Stopwatch();
		bool dataReceived = false;

		context._tcpTestClient.CreateTcpConnection(
			context,
			connectionEstablished: conn => {
				context.Log.Information("[{remoteEndPoint}, L{localEndPoint}]: Writing...", conn.RemoteEndPoint,
					conn.LocalEndPoint);
				sw.Start();
				conn.EnqueueSend(package.AsByteArray());
			},
			handlePackage: (conn, pkg) => {
				if (pkg.Command != TcpCommand.WriteEventsCompleted) {
					context.Fail(reason: string.Format("Unexpected TCP package: {0}.", pkg.Command));
					return;
				}

				dataReceived = true;
				sw.Stop();

				var dto = pkg.Data.Deserialize<WriteEventsCompleted>();
				if (dto.Result == OperationResult.Success) {
					context.Log.Information("Successfully written. EventId: {correlationId}.", package.CorrelationId);
					PerfUtils.LogTeamCityGraphData(string.Format("{0}-latency-ms", Keyword),
						(int)Math.Round(sw.Elapsed.TotalMilliseconds));
				} else {
					context.Log.Information("Error while writing: {message} ({e}).", dto.Message, dto.Result);
				}

				context.Log.Information("Write request took: {elapsed}.", sw.Elapsed);
				conn.Close();
				context.Success();
			},
			connectionClosed: (connection, error) => {
				if (dataReceived && error == SocketError.Success)
					context.Success();
				else
					context.Fail();
			});

		context.WaitForCompletion();
		return true;
	}

	private string GenerateTestData() {
		return Codec.Json.To(new TestData(Guid.NewGuid().ToString(), _random.Next(1, 101)));
	}
}

internal class TestData {
	public string Name { get; set; }
	public int Version { get; set; }

	public TestData() {
	}

	public TestData(string name, int version) {
		Name = name;
		Version = version;
	}
}
