// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using EventStore.Core.Services.Transport.Tcp;
using EventStore.Transport.Tcp;
using KurrentDB.TestClient.Commands.RunTestScenarios;
using ILogger = Serilog.ILogger;

namespace KurrentDB.TestClient.Commands;

internal class RunTestScenariosProcessor : ICmdProcessor {
	private static readonly ILogger Log = Serilog.Log.ForContext<RunTestScenariosProcessor>();
	private const string AllScenariosFlag = "ALL";

	public string Keyword {
		get { return "RT"; }
	}

	public string Usage {
		get {
			const string usage = "<max concurrent requests, int> " +
			                     "\n<connections, int> " +
			                     "\n<streams count, int> " +
			                     "\n<eventsPerStream, int> " +
			                     "\n<streams delete step, int> " +
			                     "\n<scenario name, string, " + AllScenariosFlag + " for all scenarios>" +
			                     "\n<execution period minutes, int>" +
			                     "\n<dbParentPath or custom node, string or ip:tcp:http>";

			return usage;
		}
	}

	public bool Execute(CommandProcessorContext context, string[] args) {
		if (args.Length != 0 && false == (args.Length == 7 || args.Length == 8))
			return false;

		var maxConcurrentRequests = 20;
		var connections = 10;
		var streams = 100;
		var eventsPerStream = 400;
		var streamDeleteStep = 7;
		var scenarioName = "";
		var executionPeriodMinutes = 2;
		string dbParentPath = null;
		NodeConnectionInfo customNode = null;

		{
			if (args.Length == 9) {
				throw new ArgumentException(
					"Not compatible arguments, only one of <dbPath> or <custom node> can be specified.");
			}

			if (args.Length == 8) {
				IPAddress ip;
				int tcpPort;
				int httpPort;

				var atoms = args[7].Split(':');
				if (atoms.Length == 3
				    && IPAddress.TryParse(atoms[0], out ip)
				    && int.TryParse(atoms[1], out tcpPort)
				    && int.TryParse(atoms[2], out httpPort)) {
					customNode = new NodeConnectionInfo(ip, tcpPort, httpPort);

					args = CutLastArgument(args);
				}
			}

			if (args.Length == 7 || args.Length == 8) {
				try {
					maxConcurrentRequests = int.Parse(args[0]);
					connections = int.Parse(args[1]);
					streams = int.Parse(args[2]);
					eventsPerStream = int.Parse(args[3]);
					streamDeleteStep = int.Parse(args[4]);
					scenarioName = args[5];
					executionPeriodMinutes = int.Parse(args[6]);

					if (args.Length == 8) {
						dbParentPath = args[7];
					} else {
						var envDbPath = Environment.GetEnvironmentVariable("KURRENTDB_DATABASEPATH");
						if (!string.IsNullOrEmpty(envDbPath))
							dbParentPath = envDbPath;
					}
				} catch (Exception e) {
					Log.Error("Invalid arguments ({e})", e.Message);
					return false;
				}
			}
		}

		context.IsAsync();

		Log.Information("\n---" +
		         "\nRunning scenario {scenario} using {connections} connections with {maxConcurrentRequests} max concurrent requests," +
		         "\nfor {streams} streams {eventsPerStream} events each deleting every {streamDeleteStep}th stream. " +
		         "\nExecution period {executionPeriod} minutes. " +
		         "\nDatabase path {dbParentPath};" +
		         "\nCustom Node {customNode};" +
		         "\n---",
			scenarioName,
			connections,
			maxConcurrentRequests,
			streams,
			eventsPerStream,
			streamDeleteStep,
			executionPeriodMinutes,
			dbParentPath,
			customNode);

		var directTcpSender = CreateDirectTcpSender(context);
		var allScenarios = new IScenario[] {
			new LoopingScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
			new ProjectionsKillScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				dbParentPath,
				customNode),
			new ProjForeachForcedCommonNameScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
			new ProjForeachForcedCommonNameNoRestartScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
			new LoopingProjTranWriteScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
			new LoopingProjectionKillScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
			new MassProjectionsScenario(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				dbParentPath,
				customNode),
			new ProjectionWrongTagCheck(directTcpSender,
				maxConcurrentRequests,
				connections,
				streams,
				eventsPerStream,
				streamDeleteStep,
				TimeSpan.FromMinutes(executionPeriodMinutes),
				dbParentPath,
				customNode),
		};

		Log.Information("Found scenarios {scenariosCount} total :\n{scenarios}.", allScenarios.Length,
			allScenarios.Aggregate(new StringBuilder(),
				(sb, s) => sb.AppendFormat("{0}, ", s.GetType().Name)));
		var scenarios = allScenarios.Where(x => scenarioName == AllScenariosFlag
		                                        || x.GetType().Name.Equals(scenarioName,
			                                        StringComparison.InvariantCultureIgnoreCase))
			.ToArray();

		Log.Information("Running test scenarios ({scenarios} total)...", scenarios.Length);

		foreach (var scenario in scenarios) {
			using (scenario) {
				try {
					Log.Information("Run scenario {type}", scenario.GetType().Name);
					scenario.Run();
					scenario.Clean();
					Log.Information("Scenario run successfully");
				} catch (Exception e) {
					context.Fail(e);
				}
			}
		}

		Log.Information("Finished running test scenarios");

		if (context.ExitCode == 0)
			context.Success();

		return true;
	}

	private static string[] CutLastArgument(string[] args) {
		var cutArgs = new string[7];
		Array.Copy(args, cutArgs, 7);
		return cutArgs;
	}

	private Action<IPEndPoint, byte[]> CreateDirectTcpSender(CommandProcessorContext context) {
		const int timeoutMilliseconds = 4000;

		Action<IPEndPoint, byte[]> sender = (tcpEndPoint, bytes) => {
			var sent = new AutoResetEvent(false);

			Action<TcpTypedConnection<byte[]>, TcpPackage> handlePackage = (_, __) => { };
			Action<TcpTypedConnection<byte[]>> established = connection => {
				connection.EnqueueSend(bytes);
				connection.Close();
				sent.Set();
			};
			Action<TcpTypedConnection<byte[]>, SocketError> closed = (_, __) => sent.Set();

			context._tcpTestClient.CreateTcpConnection(context, handlePackage, established, closed, false, tcpEndPoint);
			if (!sent.WaitOne(timeoutMilliseconds))
				throw new ApplicationException("Connection to server was not closed in time.");
		};

		return sender;
	}
}
