// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.AutoScavenge.Converters;
using EventStore.AutoScavenge.Domain;
using EventStore.POC.IO.Core.Serialization;
using NCrontab;
using Serilog;

namespace EventStore.AutoScavenge.Clients;

// Allows users to manage the autoscavenge from any node. Proxies the request on to the leader.
public class ProxyAutoScavengeClient(HttpClientWrapper wrapper) : GossipAwareBase, IAutoScavengeClient {
	private static readonly ILogger Log = Serilog.Log.ForContext<ProxyAutoScavengeClient>();

	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = {
			new EnumConverterWithDefault<AutoScavengeStatus>(),
			new EnumConverterWithDefault<AutoScavengeStatusResponse.Status>(),
			new CrontableScheduleJsonConverter(),
		},
	};

	public async Task<Response<AutoScavengeStatusResponse>> GetStatus(CancellationToken token) {
		try {
			var baseUrl = GetLeaderBaseUrl(nameof(GetStatus));
			if (baseUrl is null)
				return Response.ServerError<AutoScavengeStatusResponse>("No leader node was found in the cluster");

			var resp = await wrapper.HttpClient.GetFromJsonAsync<AutoScavengeStatusResponse>(
				$"{baseUrl}/auto-scavenge/status", JsonSerializerOptions, token);

			return Response.Successful(resp!);
		} catch (HttpRequestException ex) {
			var code = (int)(ex.StatusCode ?? HttpStatusCode.ServiceUnavailable);
			return code is >= 400 and < 500
				? Response.Rejected<AutoScavengeStatusResponse>(ex.Message)
				: Response.ServerError<AutoScavengeStatusResponse>(ex.Message);
		} catch (JsonException ex) {
			return Response.ServerError<AutoScavengeStatusResponse>($"error when proxying request: {ex.Message}");
		} catch (Exception ex) {
			return Response.ServerError<AutoScavengeStatusResponse>(ex.Message);
		}
	}

	public async Task<Response<Unit>> Pause(CancellationToken token) {
		try {
			var baseUrl = GetLeaderBaseUrl(nameof(Pause));
			if (baseUrl is null)
				return Response.ServerError<Unit>("No leader node was found in the cluster");

			var resp = await wrapper.HttpClient.PostAsync($"{baseUrl}/auto-scavenge/pause", null, token);
			resp.EnsureSuccessStatusCode();

			return Response.Accepted();
		} catch (HttpRequestException ex) {
			var code = (int)(ex.StatusCode ?? HttpStatusCode.ServiceUnavailable);
			return code is >= 400 and < 500
				? Response.Rejected<Unit>(ex.Message)
				: Response.ServerError<Unit>(ex.Message);
		} catch (Exception ex) {
			return Response.ServerError(ex.Message);
		}
	}

	public async Task<Response<Unit>> Resume(CancellationToken token) {
		try {
			var baseUrl = GetLeaderBaseUrl(nameof(Resume));
			if (baseUrl is null)
				return Response.ServerError<Unit>("No leader node was found in the cluster");

			var resp = await wrapper.HttpClient.PostAsync($"{baseUrl}/auto-scavenge/resume", null, token);
			resp.EnsureSuccessStatusCode();

			return Response.Accepted();
		} catch (HttpRequestException ex) {
			var code = (int)(ex.StatusCode ?? HttpStatusCode.ServiceUnavailable);
			return code is >= 400 and < 500
				? Response.Rejected<Unit>(ex.Message)
				: Response.ServerError<Unit>(ex.Message);
		} catch (Exception ex) {
			return Response.ServerError(ex.Message);
		}
	}

	public async Task<Response<Unit>> Configure(CrontabSchedule schedule, CancellationToken token) {
		try {
			var baseUrl = GetLeaderBaseUrl(nameof(Configure));
			if (baseUrl is null)
				return Response.ServerError<Unit>("No leader node was found in the cluster");

			var content = JsonContent.Create(new JsonObject {
				["schedule"] = schedule.ToString(),
			});

			var resp = await wrapper.HttpClient.PostAsync($"{baseUrl}/auto-scavenge/configure", content, token);
			resp.EnsureSuccessStatusCode();

			return Response.Accepted();
		} catch (HttpRequestException ex) {
			var code = (int)(ex.StatusCode ?? HttpStatusCode.ServiceUnavailable);
			return code is >= 400 and < 500
				? Response.Rejected<Unit>(ex.Message)
				: Response.ServerError<Unit>(ex.Message);
		} catch (Exception ex) {
			return Response.ServerError(ex.Message);
		}
	}

	private string? GetLeaderBaseUrl(string request) {
		if (!TryGetCurrentLeader(out var leaderNode, out var isSelf) || isSelf)
			return null;

		var leaderBase = $"{wrapper.Protocol}://{leaderNode.Host}:{leaderNode.Port}";
		Log.Debug("Forwarding AutoScavenge request \"{Request}\" to leader at {LeaderBase}", request, leaderBase);
		return leaderBase;
	}
}
