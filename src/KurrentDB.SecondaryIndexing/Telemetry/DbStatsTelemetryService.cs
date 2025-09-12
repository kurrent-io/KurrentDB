// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Stats;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.SecondaryIndexing.Telemetry;

public class DbStatsTelemetryService(StatsService statsService, Action<Dictionary<string, object?>> publish) : BackgroundService {
	protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
		try {
			await Task.Delay(TimeSpan.FromMinutes(50), stoppingToken);
			CollectDbStats();
			while (!stoppingToken.IsCancellationRequested) {
				await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
				CollectDbStats();
			}
		} catch (OperationCanceledException) {
			// ignore
		}
	}

	private void CollectDbStats() {
		var (streams, events) = statsService.GetTotalStats();
		var explicitTransactions = statsService.GetExplicitTransactions();
		var tCount = explicitTransactions.Count > 0 ? explicitTransactions.Sum(x => x.TransactionCount) : 0;
		var telemetry = new Dictionary<string, object?> {
			{ "streams", streams },
			{ "events", events },
			{ "explicitTransactions", tCount },
		};
		if (tCount > 0) {
			telemetry.Add("explicitTransactionLastSeen", explicitTransactions.Max(x => x.LastTransactionDate));
		}

		publish(telemetry);
	}
}
