// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using KurrentDB.AutoScavenge.Serialization;
using KurrentDB.POC.IO.Core;
using KurrentDB.POC.IO.Core.Serialization;
using Serilog;

namespace KurrentDB.AutoScavenge.Scavengers;

/// Performs remote node scavenge operations.
public class HttpNodeScavenger : INodeScavenger {
	private static readonly ILogger Log = Serilog.Log.ForContext<HttpNodeScavenger>();

	private readonly HttpClientWrapper _wrapper;
	private readonly IClient _client;

	public HttpNodeScavenger(HttpClientWrapper wrapper, IClient client) {
		_wrapper = wrapper;
		_client = client;
	}

	public async Task<Guid?> TryStartScavengeAsync(string host, int port, CancellationToken token) {
		Log.Information("Starting node scavenge on node {Host}:{Port}...", host, port);

		try {
			var resp = await _wrapper.HttpClient.PostAsync($"{_wrapper.Protocol}://{host}:{port}/admin/scavenge", null, token);
			resp.EnsureSuccessStatusCode();
			var record = await resp.Content.ReadFromJsonAsync(AutoScavengeJsonContext.Default.ScavengeRecord, token);
			var scavengeId = record?.ScavengeId;
			Log.Information("Started node scavenge on node {Host}:{Port}. ScavengeId: {ScavengeId}", host, port, scavengeId);
			return scavengeId;

		} catch (Exception ex) {
			Log.Information(ex, "Failed to start scavenge on node {Host}:{Port}", host, port);
			return null;
		}
	}

	public async Task<ScavengeStatus> TryGetScavengeAsync(string host, int port, Guid scavengeId, CancellationToken token) {
		Log.Verbose("Getting node scavenge status from node {Host}:{Port}...", host, port);

		try {
			var httpResponse = await _wrapper.HttpClient.GetAsync($"{_wrapper.Protocol}://{host}:{port}/admin/scavenge/last",
				token);

			httpResponse.EnsureSuccessStatusCode();
			var response = (await httpResponse.Content.ReadFromJsonAsync(AutoScavengeJsonContext.Default.LastScavengeStatusResponse, token))!;

			var result = await ScavengeLog.GetStatusAsync(_client, scavengeId, response.ScavengeId, response.ScavengeResult, token);

			Log.Verbose("Got node scavenge status from node {Host}:{Port}: {Result}", host, port, result);
			return result;

		} catch (Exception ex) {
			Log.Information(ex, "Failed to get scavenge status on node {Host}:{Port}", host, port);
			return ScavengeStatus.Unknown;
		}
	}

	public async Task<bool> TryPauseScavengeAsync(string host, int port, Guid? scavengeId, CancellationToken token) {
		Log.Information("Stopping node scavenge {ScavengeId} on node {Host}:{Port}...", scavengeId, host, port);

		try {
			var scavengeIdString = scavengeId?.ToString() ?? "current";
			var resp = await _wrapper.HttpClient.DeleteAsync($"{_wrapper.Protocol}://{host}:{port}/admin/scavenge/{scavengeIdString}", token);
			resp.EnsureSuccessStatusCode();
			Log.Information("Stopped node scavenge on node {Host}:{Port}", host, port);
			return true;

		} catch (HttpRequestException ex) {
			if (ex.StatusCode == HttpStatusCode.NotFound) {
				// Valid case of the scavenge successfully paused because by not being found, it means the scavenge has
				// completed. Whether the scavenge completed with an error or a success is not important in that case.
				Log.Information("Node scavenge {ScavengeId} on node {Host}:{Port} was already stopped", scavengeId, host, port);
				return true;
			} else {
				Log.Information(ex, "Failed to stop scavenge on node {Host}:{Port} > {Status}", host, port, ex.StatusCode);
				return false;
			}

		} catch (Exception ex) {
			Log.Information(ex, "Failed to stop scavenge on node {Host}:{Port}", host, port);
			return false;
		}
	}

	internal class ScavengeRecord {
		public Guid? ScavengeId { get; init; }
	}

	internal class LastScavengeStatusResponse {
		public Guid? ScavengeId { get; init; }
		[JsonConverter(typeof(EnumConverterWithDefault<LastScavengeResult>))]
		public required LastScavengeResult ScavengeResult { get; init; }
	}
}
