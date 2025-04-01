// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text.Json;
using EventStore.AutoScavenge.Converters;
using EventStore.POC.IO.Core;

namespace EventStore.AutoScavenge.Sources;

public class ClientSource : ISource {
	// todo - We should probably use the same serialization options across the board.
	private static readonly JsonSerializerOptions JsonSerializerOptions = new() {
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new CrontableScheduleJsonConverter() }
	};

	private readonly IClient _client;

	public ClientSource(IClient client) {
		_client = client;
	}

	public long AutoScavengeStreamExpectedRevision { get; set; } = -1;

	public async Task<Events.ConfigurationUpdated?> ReadConfigurationEvent(CancellationToken token) {
		var events = _client
			.ReadStreamBackwards(
				StreamNames.AutoScavengeConfiguration,
				maxCount: 1,
				token)
			.HandleStreamNotFound();

		await foreach (var @event in events) {
			if (@event.EventType != EventTypes.ConfigurationUpdated)
				throw new Exception($"Expected to find event of type {EventTypes.ConfigurationUpdated} but found {@event.EventType}");

			var configurationUpdated =
				JsonSerializer.Deserialize<Events.ConfigurationUpdated>(
					@event.Data.Span,
					JsonSerializerOptions);

			return configurationUpdated!;
		}

		return null;
	}

	public async IAsyncEnumerable<IEvent> ReadAutoScavengeEvents([EnumeratorCancellation] CancellationToken token) {
		AutoScavengeStreamExpectedRevision = -1;

		var events = _client
			.ReadStreamBackwards(
				StreamNames.AutoScavenges,
				maxCount: long.MaxValue,
				token)
			.HandleStreamNotFound();

		// in practice, there should not be too many events until we reach a `ClusterScavengeCompleted` event or the
		// beginning of the stream, so we can keep the events in memory.
		var requiredEvents = new List<IEvent>();
		var isLatestEvent = true;

		await foreach (var @event in events) {
			if (isLatestEvent) {
				AutoScavengeStreamExpectedRevision = (long)@event.EventNumber;
				isLatestEvent = false;
			}

			// after completing a cluster scavenge, auto-scavenge is always in an idle state, so we can always rehydrate
			// the auto-scavenge state machine from the next event onwards.
			if (@event.EventType is EventTypes.ClusterScavengeCompleted)
				break;

			IEvent? deserialized = @event.EventType switch {
				EventTypes.ClusterMembersChanged => JsonSerializer.Deserialize<Events.ClusterMembersChanged>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.ClusterScavengeStarted => JsonSerializer.Deserialize<Events.ClusterScavengeStarted>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.ClusterScavengeCompleted => JsonSerializer.Deserialize<Events.ClusterScavengeCompleted>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.NodeDesignated => JsonSerializer.Deserialize<Events.NodeDesignated>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.NodeScavengeStarted => JsonSerializer.Deserialize<Events.NodeScavengeStarted>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.NodeScavengeCompleted => JsonSerializer.Deserialize<Events.NodeScavengeCompleted>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.PauseRequested => JsonSerializer.Deserialize<Events.PauseRequested>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.Paused => JsonSerializer.Deserialize<Events.Paused>(@event.Data.Span, JsonSerializerOptions),
				EventTypes.Resumed => JsonSerializer.Deserialize<Events.Resumed>(@event.Data.Span, JsonSerializerOptions),
				_ => null,
			};

			if (deserialized is not null)
				requiredEvents.Add(deserialized);
		}

		for (var i = requiredEvents.Count - 1; i >=0 ; i--)
			yield return requiredEvents[i];
	}
}
