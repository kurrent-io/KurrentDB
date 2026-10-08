// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using KurrentDB.Client;
using Serilog;
using Serilog.Events;

namespace KurrentDB.Embedded.Sample;

internal static class Program {
	public static async Task Main(string[] args) {
		var ct = ConsoleCancellation();

		// The node logs through the static Serilog logger for now.
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
			.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		// DatabaseOptions is the way through to anything the server understands that this library has no
		// opinion about. The keys are flat: KurrentDB:WriteTimeoutMs, even though the server groups that
		// option under Database — KurrentDB:Database:WriteTimeoutMs would bind nothing, and would not be
		// reported either, because a nested key is how plugin configuration reaches its plugin
		// (KurrentDB:Licensing:LicenseKey).
		await using (var db = new EmbeddedKurrentDB(new() {
			DataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "kdb-embedded"),
			DatabaseOptions = new Dictionary<string, string?> {
				["KurrentDB:WriteTimeoutMs"] = "5000",
				["KurrentDB:PrepareTimeoutMs"] = "5000",
				["KurrentDB:CommitTimeoutMs"] = "5000",
			}})) {

			await db.StartAsync();

			Log.Information(
				"Started the embedded database in {DataDirectory}. Connect to unix domain socket {UnixSocket}",
				db.DataDirectory,
				db.UnixSocketPath);

			// The regular .NET client, pointed at the socket. Nothing else about using it changes.
			await using var client = new KurrentDBClient(db.CreateClientSettings());

			try {
				var data = new SampleData();

				Log.Information("Running the samples for run {runId}", data.RunId);

				Log.Information("Appending...");
				await new AppendSamples(client, data).RunAsync(ct);
				Log.Information("Reading...");
				await new ReadSamples(client, data).RunAsync(ct);
				Log.Information("Reading Indexes...");
				await new IndexSamples(client, data).RunAsync(ct);
				Log.Information("Subscribing...");
				await new SubscriptionSamples(client, data).RunAsync(ct);
				Log.Information("Managing...");
				await new StreamManagementSamples(client, data).RunAsync(ct);

				Log.Information("Done. Press Ctrl+C to stop.");
				await ct.WaitAsync();
			} catch (OperationCanceledException) {
				// Ctrl+C
			}

			Log.Information("Stopping...");
		}

		Log.Information("Stopped!");
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
