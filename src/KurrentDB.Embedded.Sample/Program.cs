// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Client;
using Serilog;
using Serilog.Events;

namespace KurrentDB.Embedded.Sample;

internal static class Program {
	public static async Task Main(string[] args) {
		// The node logs through the static Serilog logger.
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		var cancellation = ConsoleCancellation();

		var dataDirectory = args.Length > 0
			? args[0]
			: Path.Combine(Directory.GetCurrentDirectory(), "kdb-embedded");

		// DatabaseOptions is the way through to anything the server understands that this library has no
		// opinion about. The keys are flat: KurrentDB:WriteTimeoutMs, even though the server groups that
		// option under Database — KurrentDB:Database:WriteTimeoutMs would bind nothing, and would not be
		// reported either, because a nested key is how plugin configuration reaches its plugin
		// (KurrentDB:Licensing:LicenseKey).
		await using var db = new EmbeddedKurrentDB(new() {
			Name = "DB1",
			DataDirectory = dataDirectory,
			DatabaseOptions = new Dictionary<string, string?> {
				["KurrentDB:WriteTimeoutMs"] = "5000",
				["KurrentDB:PrepareTimeoutMs"] = "5000",
				["KurrentDB:CommitTimeoutMs"] = "5000",
			},
		});

		await db.StartAsync();

		Log.Information(
			"Started the embedded database in {DataDirectory}. Connect to unix domain socket {UnixSocket}",
			db.DataDirectory,
			db.UnixSocketPath);

		// The regular .NET client, pointed at the socket. Nothing else about using it changes.
		await using var client = new KurrentDBClient(db.CreateClientSettings());

		var data = new SampleData();

		Log.Information("Running the samples for run {runId}", data.RunId);

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

	static CancellationToken ConsoleCancellation() {
		var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) => {
			eventArgs.Cancel = true;
			cts.Cancel();
		};
		return cts.Token;
	}
}
