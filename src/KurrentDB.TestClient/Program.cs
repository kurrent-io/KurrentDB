// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Log;
using EventStore.Common.Options;
using EventStore.Common.Utils;
using KurrentDB.TestClient.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

#nullable enable
namespace KurrentDB.TestClient;

internal enum StatsFormat {
	Csv,
	Json
}
internal static class Program {
	/// <summary>
	///
	/// </summary>
	/// <param name="version">Show version.</param>
	/// <param name="log">Path where to keep log files.</param>
	/// <param name="whatIf">Print effective configuration to console and then exit.</param>
	/// <param name="command"></param>
	/// <param name="host">The host to bind to.</param>
	/// <param name="tcpPort">The port to run the TCP server on.</param>
	/// <param name="httpPort">The port to run the HTTP server on.</param>
	/// <param name="timeout"></param>
	/// <param name="readWindow"></param>
	/// <param name="writeWindow"></param>
	/// <param name="pingWindow"></param>
	/// <param name="reconnect"></param>
	/// <param name="useTls"></param>
	/// <param name="tlsValidateServer"></param>
	/// <param name="connectionString">The connection string to use when connecting to the server. Not used by the raw TCP client</param>
	/// <param name="statsFormat">The format for the stats log.</param>
	/// <returns></returns>
	public static async Task<int> Main(bool version = false, FileInfo? log = null, bool whatIf = false,
		string[]? command = null, string host = "localhost", int tcpPort = 1113, int httpPort = 2113,
		int timeout = Timeout.Infinite, int readWindow = 2000, int writeWindow = 2000, int pingWindow = 2000,
		bool reconnect = true, bool useTls = false, bool tlsValidateServer = false, string connectionString = "",
		StatsFormat statsFormat = StatsFormat.Csv) {
		Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;

		try {
			var logsDirectory = log?.FullName ?? Locations.DefaultTestClientLogDirectory;
			EventStoreLoggerConfiguration.Initialize(logsDirectory, "client", LogConsoleFormat.Plain,
				1024 * 1024 * 1024, RollingInterval.Day, 31, false);
			var statsLog = statsFormat == StatsFormat.Csv
				? TestClientCsvLoggerConfiguration.Initialize(logsDirectory, "client")
				: Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "REGULAR-STATS-LOGGER");

			var options = new ClientOptions {
				Timeout = timeout,
				HttpPort = httpPort,
				Host = host,
				TcpPort = tcpPort,
				Reconnect = reconnect,
				PingWindow = pingWindow,
				ReadWindow = readWindow,
				WriteWindow = writeWindow,
				UseTls = useTls,
				TlsValidateServer = tlsValidateServer,
				ConnectionString = connectionString,
				Command = command ?? Array.Empty<string>(),
				OutputCsv = statsFormat == StatsFormat.Csv,
				StatsLog = statsLog
			};

			var hostedService = new TestClientHostedService(options);

			if (whatIf) {
				await Console.Out.WriteLineAsync(options.ToString());
			}

			if (version || whatIf) {
				await Console.Out.WriteLineAsync(VersionInfo.Text);

				await Console.Out.FlushAsync();

				return 0;
			}
			await CreateHostBuilder(hostedService, Environment.GetCommandLineArgs())
				.RunConsoleAsync(options => options.SuppressStatusMessages = true, hostedService.CancellationToken);
			return await hostedService.Exited;
		} catch (Exception ex) {
			Log.Fatal(ex, "Host terminated unexpectedly.");
			return 1;
		} finally {
			await Log.CloseAndFlushAsync();
		}
	}

	private static IHostBuilder CreateHostBuilder(IHostedService hostedService, string[] args) =>
		new HostBuilder()
			.ConfigureHostConfiguration(builder =>
				builder.AddEnvironmentVariables("DOTNET_").AddCommandLine(args))
			.ConfigureAppConfiguration(builder =>
				builder.AddEnvironmentVariables().AddCommandLine(args))
			.ConfigureServices(services => services.AddSingleton(hostedService))
			.ConfigureLogging(logging => logging.AddSerilog());
}
