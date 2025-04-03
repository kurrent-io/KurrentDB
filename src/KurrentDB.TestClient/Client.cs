// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Text;
using System.Threading;
using EventStore.Common.Utils;
using KurrentDB.TestClient.Commands;
using KurrentDB.TestClient.Commands.DvuBasic;
using Connection = EventStore.Transport.Tcp.TcpTypedConnection<byte[]>;
using ILogger = Serilog.ILogger;
#pragma warning disable 1591

namespace KurrentDB.TestClient;

public class Client {
	public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromMilliseconds(1000);
	private static readonly ILogger Log = Serilog.Log.ForContext<Client>();

	public readonly bool InteractiveMode;

	public readonly ClientOptions Options;

	public readonly TcpTestClient _tcpTestClient;
	public readonly GrpcTestClient _grpcTestClient;
	public readonly ClientApiTcpTestClient _clientApiTestClient;

	private readonly CommandsProcessor _commands = new CommandsProcessor(Log);

	public Client(ClientOptions options, CancellationTokenSource cancellationTokenSource) {
		Options = options;

		var interactiveMode = options.Command.IsEmpty();

		InteractiveMode = options.Command.IsEmpty();

		_tcpTestClient = new TcpTestClient(options, interactiveMode, Log);
		_grpcTestClient = new GrpcTestClient(options, Log);
		_clientApiTestClient = new ClientApiTcpTestClient(options, Log);

		RegisterProcessors(cancellationTokenSource);
	}

	private void RegisterProcessors(CancellationTokenSource cancellationTokenSource) {
		_commands.Register(new UsageProcessor(_commands), usageProcessor: true);
		_commands.Register(new ExitProcessor(cancellationTokenSource));

		_commands.Register(new PingProcessor());
		_commands.Register(new PingFloodProcessor());
		_commands.Register(new PingFloodWaitingProcessor());

		_commands.Register(new WriteProcessor());
		_commands.Register(new WriteJsonProcessor());
		_commands.Register(new WriteFloodProcessor());
		_commands.Register(new WriteFloodClientApiProcessor());
		_commands.Register(new WriteFloodWaitingProcessor());

		_commands.Register(new MultiWriteProcessor());
		_commands.Register(new MultiWriteFloodWaitingProcessor());

		_commands.Register(new TransactionWriteProcessor());

		_commands.Register(new DeleteProcessor());

		_commands.Register(new ReadAllProcessor());
		_commands.Register(new ReadProcessor());
		_commands.Register(new ReadFloodProcessor());

		_commands.Register(new WriteLongTermProcessor());

		_commands.Register(new DvuBasicProcessor());
		_commands.Register(new RunTestScenariosProcessor());

		_commands.Register(new SubscribeToStreamProcessor());

		_commands.Register(new ScavengeProcessor());

		_commands.Register(new TcpSanitazationCheckProcessor());

		_commands.Register(new SubscriptionStressTestProcessor());

		// gRPC
		_commands.Register(new GrpcCommands.ReadAllProcessor());
		_commands.Register(new GrpcCommands.WriteFloodProcessor());

		// TCP Client API
		_commands.Register(new ClientApiTcpCommands.WriteFloodProcessor());
	}

	public string GetCommandList() {
		var sb = new StringBuilder();
		_commands.RegisteredProcessors.Select(x => x.Usage.ToUpper()).ToList().ForEach(s => sb.AppendLine(s));
		return sb.ToString();
	}

	public int Run(CancellationToken cancellationToken) {
		if (!InteractiveMode) {
			var args = ParseCommandLine(Options.Command[0]);
			return Execute(args, cancellationToken);
		}

		new Thread(() => {
			Thread.Sleep(100);
			Console.WriteLine(GetCommandList());
			Console.Write(">>> ");

			string line;
			while ((line = Console.ReadLine()) != null) {
				try {
					if (string.IsNullOrWhiteSpace(line))
						continue;

					try {
						var args = ParseCommandLine(line);
						Execute(args, cancellationToken);
					} catch (Exception exc) {
						Log.Error(exc, "Error during executing command.");
					}
				} finally {
					Thread.Sleep(100);
					Console.Write(">>> ");
				}
			}
		}) {IsBackground = true, Name = "Client Main Loop Thread"}.Start();
		return 0;
	}

	private static string[] ParseCommandLine(string line) {
		return line.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
	}

	private int Execute(string[] args, CancellationToken cancellationToken) {
		Log.Information("Processing command: {command}.", string.Join(" ", args));

		var context = new CommandProcessorContext(_tcpTestClient, _grpcTestClient, _clientApiTestClient, Options.Timeout,
			Log, Options.StatsLog, Options.OutputCsv, new ManualResetEventSlim(true), cancellationToken);

		int exitCode;
		if (_commands.TryProcess(context, args, out exitCode)) {
			Log.Information("Command exited with code {exitCode}.", exitCode);
			return exitCode;
		}

		return exitCode;
	}
}
