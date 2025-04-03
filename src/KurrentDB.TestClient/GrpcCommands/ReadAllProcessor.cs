// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using Serilog;

namespace KurrentDB.TestClient.GrpcCommands;

internal class ReadAllProcessor : ICmdProcessor {
	public string Usage {
		get { return "RDALLGRPC [[F|B] [clients] [<commit pos> <prepare pos>]]"; }
	}

	public string Keyword {
		get { return "RDALLGRPC"; }
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		Direction direction = Direction.Forwards;
		Position position = Position.Start;
		bool forward = true;
		bool posOverriden = false;
		int clientCount = 1;

		if (args.Length > 0) {
			if (args.Length > 4)
				return false;

			if (args[0].ToUpper() == "F")
				forward = true;
			else if (args[0].ToUpper() == "B")
				forward = false;
			else
				return false;

			if (args.Length > 1) {
				clientCount = MetricPrefixValue.ParseInt(args[1]);
			}

			if (args.Length == 4) {
				posOverriden = true;
				if (!ulong.TryParse(args[2], out var commitPos) || !ulong.TryParse(args[3], out var preparePos))
					return false;

				position = new Position(commitPos, preparePos);
			}
		}

		if (!posOverriden) {
			position = forward ? Position.Start : Position.End;
		}

		context.IsAsync();

		if (context._grpcTestClient.AreCredentialsMissing) {
			context.Fail(reason: "Credentials are needed in order to read from the $all stream, specify a connection string with credentials!");
			return true;
		}
		
		var task = ReadAll(context, clientCount, direction, position);
		task.Wait();

		return true;
	}
	
	private async Task ReadAll(CommandProcessorContext context, int clientCount, Direction direction, Position position) {

		var cts = new CancellationTokenSource();

		ProgressMonitor monitor = new ProgressMonitor(context.Log);

		var clientTasks = new List<Task>();
		for (int i = 0; i < clientCount; i++) {
			if (i > 0) {
				await Task.Delay(TimeSpan.FromSeconds(30));
			}
			
			EventStoreClient client = context._grpcTestClient.CreateGrpcClient();
			clientTasks.Add(ReadAllTask(client));
		}
		
		await Task.WhenAll(clientTasks);
		
		monitor.Stop();
		context.Log.Information("=== Reading ALL {readDirection} completed in {elapsed}. Total read: {total}",
			direction, monitor.Duration, monitor.Total);
		context.Success();
		
		async Task ReadAllTask(EventStoreClient c) {
			var r = c.ReadAllAsync(direction, position, cancellationToken: cts.Token);
			await foreach (var _ in r.Messages.WithCancellation(cts.Token)) {
				monitor.Increment();
			}
		}
	}
	
	class ProgressMonitor {
		private ulong _i;
		private readonly ILogger _log;
		private readonly Stopwatch _duration;
		private readonly Stopwatch _interval;

		public ProgressMonitor (ILogger log) {
			_log = log;
			_duration = Stopwatch.StartNew();
			_interval = Stopwatch.StartNew();
		}

		public TimeSpan Duration => _duration.Elapsed;
		public ulong Total => _i;
		
		public void Increment() {
			var result = Interlocked.Increment(ref _i);

			if (result % 1000 == 0) {
				Console.Write(".");
			}
			
			if (result % 100_000 == 0) {
				_log.Information(
					"\nDONE TOTAL {reads} READ IN {elapsed} ({rate:0.0}/s)",
					_i, _interval.Elapsed, 1000.0 * (_i / _duration.Elapsed.TotalMilliseconds));
				_interval.Restart();
			}
		}

		public void Stop() {
			_duration.Stop();
			_interval.Stop();
		}
	}
}
