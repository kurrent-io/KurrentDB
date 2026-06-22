// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Components.Scavenges;

public class ScavengeService(IPublisher publisher, IAuthorizationProvider authorizer) {
	// Same operations the gRPC Operations.Scavenge service / streams read path authorize against. The UI
	// publishes core messages directly, bypassing the transport layer, so it must check the equivalent here.
	static readonly Operation StartOp = new(Operations.Node.Scavenge.Start);
	static readonly Operation StopOp = new(Operations.Node.Scavenge.Stop);

	// Reading scavenge history/detail is a stream read of the (system) $scavenges streams.
	static Operation ReadStreamOp(string streamId) => UiOperations.ReadStream(streamId);

	public async ValueTask<string> StartAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, StartOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.ScavengeDatabase(
			envelope, Guid.NewGuid(), principal,
			startFromChunk: 0, threads: 1, threshold: null, throttlePercent: null, syncOnly: false));

		return await envelope.Task.WaitAsync(ct) switch {
			ClientMessage.ScavengeDatabaseStartedResponse started => started.ScavengeId,
			ClientMessage.ScavengeDatabaseInProgressResponse inProgress =>
				throw new ScavengeException($"A scavenge is already in progress ({inProgress.ScavengeId})."),
			ClientMessage.ScavengeDatabaseUnauthorizedResponse =>
				throw new ScavengeException("Access denied."),
			_ => throw new ScavengeException("An unexpected error occurred.")
		};
	}

	public async ValueTask StopAsync(ClaimsPrincipal principal, string scavengeId, CancellationToken ct) {
		await Authorize(principal, StopOp, ct);
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.StopDatabaseScavenge(envelope, Guid.NewGuid(), principal, scavengeId));

		switch (await envelope.Task.WaitAsync(ct)) {
			case ClientMessage.ScavengeDatabaseStoppedResponse:
				return;
			case ClientMessage.ScavengeDatabaseNotFoundResponse:
				throw new ScavengeException($"Scavenge '{scavengeId}' not found.");
			case ClientMessage.ScavengeDatabaseUnauthorizedResponse:
				throw new ScavengeException("Access denied.");
			default:
				throw new ScavengeException("An unexpected error occurred.");
		}
	}

	public async ValueTask<ScavengeHistoryEntry[]> GetHistoryAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, ReadStreamOp("$scavenges"), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsBackward(
			corrId, corrId, envelope,
			eventStreamId: "$scavenges",
			fromEventNumber: -1,
			maxCount: 100,
			resolveLinkTos: true,
			requireLeader: false,
			validationStreamVersion: null,
			user: principal,
			replyOnExpired: false));

		var result = await envelope.Task.WaitAsync(ct);

		if (result.Result == ReadStreamResult.AccessDenied)
			throw new ScavengeException("Access denied to scavenge history.");

		if (result.Result is not ReadStreamResult.Success)
			return [];

		var entries = new Dictionary<string, ScavengeHistoryEntry>();

		foreach (var resolved in result.Events) {
			var e = resolved.Event;
			if (e == null)
				continue;

			try {
				using var data = JsonDocument.Parse(e.Data);
				var root = data.RootElement;

				var scavengeId = root.TryGetProperty("scavengeId", out var idProp) ? idProp.GetString() : null;
				if (string.IsNullOrEmpty(scavengeId))
					continue;

				if (!entries.TryGetValue(scavengeId, out var entry)) {
					var nodeEndpoint = root.TryGetProperty("nodeEndpoint", out var neProp) ? neProp.GetString() : "";
					entry = new ScavengeHistoryEntry { ScavengeId = scavengeId, NodeEndpoint = nodeEndpoint };
					entries[scavengeId] = entry;
				}

				switch (e.EventType) {
					case "$scavengeStarted":
						entry.StartTime = e.TimeStamp;
						break;
					case "$scavengeCompleted":
						entry.EndTime = e.TimeStamp;
						if (root.TryGetProperty("result", out var resultProp))
							entry.Result = resultProp.GetString();
						break;
				}
			} catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) {
				// Skip a malformed/unexpected $scavenges record rather than failing the whole history view.
			}
		}

		return entries.Values
			.Where(e => e.StartTime != null)
			.OrderByDescending(e => e.StartTime)
			.ToArray();
	}

	public async ValueTask<ScavengeDetailPage> GetDetailPageAsync(
		ClaimsPrincipal principal, string scavengeId, long fromEventNumber, int maxCount, CancellationToken ct) {

		await Authorize(principal, ReadStreamOp($"$scavenges-{scavengeId}"), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsBackward(
			corrId, corrId, envelope,
			eventStreamId: $"$scavenges-{scavengeId}",
			fromEventNumber: fromEventNumber,
			maxCount: maxCount,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: principal,
			replyOnExpired: false));

		var result = await envelope.Task.WaitAsync(ct);

		if (result.Result == ReadStreamResult.AccessDenied)
			throw new ScavengeException("Access denied.");

		if (result.Result is not ReadStreamResult.Success)
			return new ScavengeDetailPage([], IsEndOfStream: true, FirstEventNumber: null, LastEventNumber: null);

		var events = new List<ScavengeDetailEvent>(result.Events.Count);

		foreach (var resolved in result.Events) {
			var e = resolved.Event;
			if (e == null)
				continue;

			try {
				using var data = JsonDocument.Parse(e.Data);
				events.Add(ScavengeParsing.MapDetailEvent(e.EventType, data.RootElement));
			} catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) {
				// Skip a malformed/unexpected $scavenges record rather than failing the whole detail view.
			}
		}

		// Anchor positions come from the raw read (the actual stream positions), independent of any
		// events skipped during mapping.
		long? first = result.Events.Count > 0 ? result.Events[0].OriginalEventNumber : null;
		long? last = result.Events.Count > 0 ? result.Events[^1].OriginalEventNumber : null;
		return new ScavengeDetailPage(events, result.IsEndOfStream, first, last);
	}

	// Enforce the configured authorization policy before publishing, translating a denial into the
	// ScavengeException the components already handle.
	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new ScavengeException("Access denied.");
	}
}

public record ScavengeDetailPage(
	IReadOnlyList<ScavengeDetailEvent> Events, bool IsEndOfStream, long? FirstEventNumber, long? LastEventNumber);

public class ScavengeException(string message) : Exception(message);
