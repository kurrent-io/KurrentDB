// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using System.Text.Json;
using KurrentDB.AutoScavenge.Serialization;
using KurrentDB.POC.IO.Core;

namespace KurrentDB.AutoScavenge.Sources;

public class ClientSource : ISource {
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
				JsonSerializer.Deserialize(
					@event.Data.Span,
					AutoScavengeJsonContext.Default.ConfigurationUpdated);

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
				EventTypes.ClusterMembersChanged => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.ClusterMembersChanged),
				EventTypes.ClusterScavengeStarted => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.ClusterScavengeStarted),
				EventTypes.ClusterScavengeCompleted => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.ClusterScavengeCompleted),
				EventTypes.NodeDesignated => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.NodeDesignated),
				EventTypes.NodeScavengeStarted => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.NodeScavengeStarted),
				EventTypes.NodeScavengeCompleted => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.NodeScavengeCompleted),
				EventTypes.PauseRequested => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.PauseRequested),
				EventTypes.Paused => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.Paused),
				EventTypes.Resumed => JsonSerializer.Deserialize(@event.Data.Span, AutoScavengeJsonContext.Default.Resumed),
				_ => null,
			};

			if (deserialized is not null)
				requiredEvents.Add(deserialized);
		}

		for (var i = requiredEvents.Count - 1; i >= 0; i--)
			yield return requiredEvents[i];
	}
}
