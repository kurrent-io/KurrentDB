// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace EventStore.Core.Telemetry;


public class TelemetrySink : ITelemetrySink {
	private static readonly ILogger _log = Log.ForContext<TelemetrySink>();
	private const string ApiHost = "https://kurrent.io/telemetry";
	private readonly bool _optout;
	private readonly HttpClient _httpClient;
	private readonly JsonSerializerOptions _serializerOptions = new() {
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	public TelemetrySink(bool optout) {
		_optout = optout;

		LogTelemetryMessage();

		if (!optout) {
			_httpClient = new HttpClient();
		}
	}

	public async Task Flush(JsonObject data, CancellationToken token) {
		var json = JsonSerializer.Serialize(data, _serializerOptions);

		if (_optout) {
			_log.Information("Telemetry not sent; opted out: " + Environment.NewLine + json);
		} else {
			_log.Information("Sending telemetry data to {url} (visit for more information): " + Environment.NewLine + json, ApiHost);
			try {
				await _httpClient.PostAsync(ApiHost, JsonContent.Create(data), token);
			} catch (Exception ex) when (ex is not TaskCanceledException) {
				_log.Error("Error when sending telemetry payload: {exception}", ex);
			}
		}
	}

	private void LogTelemetryMessage() {
		var sb = new StringBuilder();

		sb.AppendLine("");
		sb.AppendLine("Telemetry");
		sb.AppendLine("---------");

		if (_optout) {
			sb.AppendLine("You have opted out of sending telemetry by setting the KURRENTDB_TELEMETRY_OPTOUT environment variable to true.");
		} else {
			sb.Append("KurrentDB collects usage data in order to improve your experience. ");
			sb.AppendLine("The data is anonymous and collected by Kurrent, Inc.");
			sb.AppendLine("You can opt out of sending telemetry by setting the KURRENTDB_TELEMETRY_OPTOUT environment variable to true.");
		}

		sb.AppendLine("For more information visit https://kurrent.io/telemetry");
		_log.Information(sb.ToString());
	}
}
