// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Components.Dashboard;

public class DashboardService(
	[FromKeyedServices(KeyedServices.MonitoringQueuePublisher)] IPublisher monitoringQueue,
	IAuthorizationProvider authorizer) {

	// The HTTP /stats endpoint (which also publishes GetFreshStats) gates on Operations.Node.Statistics.Read;
	// authorize the same way before publishing so the UI doesn't bypass the policy.
	static readonly Operation ReadStatisticsOperation = new(Operations.Node.Statistics.Read);

	public async ValueTask<Dictionary<string, object>> GetStatsAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await authorizer.EnsureAccessAsync(principal, ReadStatisticsOperation, ct);
		var envelope = new TcsEnvelope<MonitoringMessage.GetFreshStatsCompleted>();
		monitoringQueue.Publish(new MonitoringMessage.GetFreshStats(envelope, s => s, useMetadata: false, useGrouping: true));
		var result = await envelope.Task.WaitAsync(ct);
		return result.Success ? result.Stats : [];
	}
}

public class QueueStats {
	public string Name { get; init; } = "";
	public string GroupName { get; init; } = "";
	public int Length { get; init; }
	public double AvgItemsPerSecond { get; init; }
	public double AvgProcessingTime { get; init; }
	public long TotalItemsProcessed { get; init; }
	public string CurrentMessage { get; init; } = "";
	public string LastMessage { get; init; } = "";

	public static QueueStats[] FromDictionary(Dictionary<string, object> stats) {
		var result = new List<QueueStats>();

		// With useGrouping=true, stats are nested: "es" -> { "queue" -> { "QueueName" -> { metrics } } }
		Dictionary<string, object> queueDict = null;

		// Try "es-queue" (flat grouped)
		if (stats.TryGetValue("es-queue", out var q1) && q1 is Dictionary<string, object> d1)
			queueDict = d1;
		// Try "es" -> "queue" (nested grouped)
		else if (stats.TryGetValue("es", out var es) && es is Dictionary<string, object> esDict
			&& esDict.TryGetValue("queue", out var q2) && q2 is Dictionary<string, object> d2)
			queueDict = d2;

		if (queueDict != null) {
			foreach (var (queueName, qVal) in queueDict) {
				if (qVal is not Dictionary<string, object> qMetrics)
					continue;

				result.Add(new QueueStats {
					Name = queueName,
					GroupName = qMetrics.TryGetValue("groupName", out var gn) ? gn?.ToString() ?? "" : "",
					Length = GetInt(qMetrics, "lengthCurrentTryPeak"),
					AvgItemsPerSecond = GetDouble(qMetrics, "avgItemsPerSecond"),
					AvgProcessingTime = GetDouble(qMetrics, "avgProcessingTime"),
					TotalItemsProcessed = GetLong(qMetrics, "totalItemsProcessed"),
					CurrentMessage = qMetrics.TryGetValue("inProgressMessage", out var cm) ? cm?.ToString() ?? "" : "",
					LastMessage = qMetrics.TryGetValue("lastProcessedMessage", out var lm) ? lm?.ToString() ?? "" : "",
				});
			}
		}

		return result.OrderBy(q => q.Name).ToArray();
	}

	static int GetInt(Dictionary<string, object> d, string key) =>
		d.TryGetValue(key, out var v) && v is int i ? i : d.TryGetValue(key, out v) && v is long l ? (int)l : 0;

	static long GetLong(Dictionary<string, object> d, string key) =>
		d.TryGetValue(key, out var v) && v is long l ? l : d.TryGetValue(key, out v) && v is int i ? i : 0;

	static double GetDouble(Dictionary<string, object> d, string key) =>
		d.TryGetValue(key, out var v) ? v switch {
			double dbl => dbl,
			float f => f,
			long l => l,
			int i => i,
			_ => 0
		} : 0;
}
