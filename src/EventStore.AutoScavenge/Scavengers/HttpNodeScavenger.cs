// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventStore.POC.IO.Core;
using EventStore.POC.IO.Core.Serialization;
using Serilog;

namespace EventStore.AutoScavenge.Scavengers;

/// Performs remote node scavenge operations.
public class HttpNodeScavenger : INodeScavenger {
	private static readonly ILogger Log = Serilog.Log.ForContext<HttpNodeScavenger>();

	private readonly HttpClientWrapper _wrapper;
	private readonly IClient _client;

	public HttpNodeScavenger(HttpClientWrapper wrapper, IClient client) {
		_wrapper = wrapper;
		_client = client;
	}

	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = {
			new EnumConverterWithDefault<ScavengeResult>(),
			new EnumConverterWithDefault<LastScavengeStatus>(),
		},
	};

	public async Task<Guid?> TryStartScavengeAsync(string host, int port, CancellationToken token) {
		Log.Information("Starting node scavenge on node {Host}:{Port}...", host, port);

		try {
			var resp = await _wrapper.HttpClient.PostAsync($"{_wrapper.Protocol}://{host}:{port}/admin/scavenge", null, token);
			resp.EnsureSuccessStatusCode();
			var record = await resp.Content.ReadFromJsonAsync<ScavengeRecord>(JsonSerializerOptions, token);
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
			var response = (await httpResponse.Content.ReadFromJsonAsync<LastScavengeStatusResponse>(JsonSerializerOptions, token))!;

			if (response.ScavengeResult == LastScavengeStatus.Unknown) {
				// the node was restarted. we don't know if our scavenge completed successfully, so we try to read its
				// stream from the scavenge log.
				var streamResult = await GetScavengeStatusFromStream(scavengeId, token);
				Log.Verbose("Got node scavenge status for {Host}:{Port} from stream: {Result}", host, port, streamResult);
				return streamResult;
			}

			if (response.ScavengeId != scavengeId) {
				// the scavenge ids do not match. this means that a new scavenge was launched, presumably by the user,
				// definitely after the one we started. we just log a warning and continue normally, pretending that
				// this scavenge is the one we started.
				Log.Warning("The last scavenge ID: {lastScavengeId} does not match with the expected scavenge ID: {expectedScavengeId}.",
					response.ScavengeId, scavengeId);
			}

			var result = response.ScavengeResult switch {
				LastScavengeStatus.InProgress => ScavengeStatus.InProgress,
				LastScavengeStatus.Success => ScavengeStatus.Success,
				LastScavengeStatus.Errored => ScavengeStatus.Errored,
				LastScavengeStatus.Stopped => ScavengeStatus.Stopped,
				_ => ScavengeStatus.Unknown,
			};

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

	// this is only called if the node reported that it is not currently scavenging and has not scavenged since startup
	private async Task<ScavengeStatus> GetScavengeStatusFromStream(Guid scavengeId, CancellationToken token) {
		// it's possible that the `$scavengeCompleted` event is not the last event in the stream as in some cases the
		// events may be out of order. so, we read more than one event to increase the chance of finding it if it is there.
		var events = _client
			.ReadStreamBackwards($"$scavenges-{scavengeId}", maxCount: 20, token)
			.HandleStreamNotFound();

		await foreach (var @event in events) {
			if (@event.EventType != "$scavengeCompleted")
				continue;

			var completed =
				JsonSerializer.Deserialize<ScavengeRecordCompleted>(@event.Data.Span, JsonSerializerOptions)!;

			if (completed.ScavengeId != scavengeId) {
				// ignore, malformed entry
				continue;
			}

			switch (completed.Result) {
				case ScavengeResult.Unknown:
					return ScavengeStatus.NotRunningUnknown;
				case ScavengeResult.Success:
					return ScavengeStatus.Success;
				case ScavengeResult.Stopped:
					return ScavengeStatus.Stopped;
				case ScavengeResult.Errored:
					return ScavengeStatus.Errored;
				case ScavengeResult.Interrupted:
					return ScavengeStatus.NotRunningUnknown;
			}
		}

		// at this point, we cannot know the status of the scavenge for sure:
		// entries may be missing from the scavenge log even if a scavenge had completed, for example:
		// i)   a cluster loses its leader after a scavenge has started on a node. the scavenge continues but
		//      events can no longer be written to the scavenge log.
		// ii)  a network partition separates a node from the cluster after a scavenge started on that node. the node
		//      won't be able to forward scavenge log events to the leader.

		return ScavengeStatus.NotRunningUnknown;
	}

	// Names match the status in the $scavengeCompleted event from the server (apart from Unknown)
	private enum ScavengeResult {
		Unknown,
		Success,
		Stopped,
		Errored,
		Interrupted,
	}

	private class ScavengeRecord {
		public Guid? ScavengeId { get; init; }
	}

	private class ScavengeRecordCompleted {
		public required Guid ScavengeId { get; init; }
		public required ScavengeResult Result { get; init; }
	}

	// Names match the result from the server
	private enum LastScavengeStatus {
		Unknown,
		InProgress,
		Success,
		Stopped,
		Errored,
	}

	private class LastScavengeStatusResponse {
		public Guid? ScavengeId { get; init; }
		public required LastScavengeStatus ScavengeResult { get; init; }
	}
}
