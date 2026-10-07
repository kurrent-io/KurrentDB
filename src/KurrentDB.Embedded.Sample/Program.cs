// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using Serilog;
using Serilog.Events;

namespace KurrentDB.Embedded.Sample;

internal static class Program {
	public static async Task Main(string[] args) {
		// The node logs through the static Serilog logger, so this is what decides whether an embedded
		// database says anything at all.
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		var dataDirectory = args.Length > 0
			? args[0]
			: Path.Combine(Directory.GetCurrentDirectory(), "kdb-embedded");

		await using var db = new EmbeddedKurrentDB(new() {
			DataDirectory = dataDirectory,
		});

		Log.Information("Starting the embedded database in {dataDirectory}...",
			db.DataDirectory);

		await db.StartAsync();

		Log.Information("Socket:   {unixSocket}", db.UnixSocketPath);
		Log.Information("HTTP:     http://localhost:{port}", db.ServerOptions.Interface.NodePort);

		// The regular .NET client, pointed at the socket. Nothing else about using it changes.
		await using var client = new KurrentDBClient(db.CreateClientSettings());

		var data = new SampleData();
		var cancellation = ConsoleCancellation();

		Log.Information("Running the samples for run {runId}", data.RunId);

		_ = Foo();
		await new AppendSamples(client, data).RunAsync(cancellation);
		await new ReadSamples(client, data).RunAsync(cancellation);
		await new SubscriptionSamples(client, data).RunAsync(cancellation);
		await new StreamManagementSamples(client, data).RunAsync(cancellation);
		await new IndexSamples(client, data).RunAsync(cancellation);

		Log.Information("Done. Press Ctrl+C to stop.");
		try {
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
		} catch (OperationCanceledException) {
			// Ctrl+C
		}

		Log.Information("Stopping...");
	}

	static async Task Foo() {
		while (true) {
			await Task.Delay(10_000);
			Log.Information("collecting");
			GC.Collect(2, GCCollectionMode.Default, true, true);
		}
	}

	static CancellationToken ConsoleCancellation() {
		var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) => {
			eventArgs.Cancel = true;
			cts.Cancel();
		};
		return cts.Token;
	}
}
