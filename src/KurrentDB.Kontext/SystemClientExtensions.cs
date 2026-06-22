// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Kontext.Mcp;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.UserManagement;
using Polly;

namespace KurrentDB.Kontext;

public readonly record struct SubscriptionMessage(
	ResolvedEvent? Event,
	(ulong Commit, ulong Prepare)? Checkpoint,
	bool CaughtUp);

public static class SystemClientExtensions {
	public static async IAsyncEnumerable<SubscriptionMessage> SubscribeToAll(
		this ISystemClient systemClient,
		(ulong Commit, ulong Prepare)? checkpoint,
		IEventFilter filter,
		[EnumeratorCancellation] CancellationToken ct) {
		var startPosition = checkpoint.HasValue
			? new Position(checkpoint.Value.Commit, checkpoint.Value.Prepare)
			: (Position?)null;

		var channel = Channel.CreateBounded<ReadResponse>(new BoundedChannelOptions(64) {
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait,
		});

		await systemClient.Subscriptions.SubscribeToAll(
			startPosition, filter, maxSearchWindow: 32,
			channel, ResiliencePipeline.Empty, ct);

		var isCaughtUp = false;
		await foreach (var response in channel.Reader.ReadAllAsync(ct)) {
			if (response is ReadResponse.EventReceived { Event: var resolved })
				yield return new SubscriptionMessage { Event = resolved };
			else if (response is ReadResponse.CheckpointReceived cp)
				yield return new SubscriptionMessage { Checkpoint = (cp.CommitPosition, cp.PreparePosition) };
			else if (!isCaughtUp && response is ReadResponse.SubscriptionCaughtUp) {
				isCaughtUp = true;
				yield return new SubscriptionMessage { CaughtUp = true };
			}
		}
	}

	public static async Task<ulong> GetHeadPositionAsync(this ISystemClient systemClient, CancellationToken ct = default) {
		var lastPosition = await systemClient.GetLastPosition(ct);
		return lastPosition >= 0 ? (ulong)lastPosition : 0;
	}

	public static async IAsyncEnumerable<ResolvedEvent> ReadAsync(
		this ISystemClient systemClient, string stream, long eventNumber, int count = 1) {
		await using var enumerator = systemClient.Reading
			.ReadStreamForwards(stream, StreamRevision.FromInt64(eventNumber), count)
			.GetAsyncEnumerator();

		while (true) {
			try {
				if (!await enumerator.MoveNextAsync())
					yield break;
			} catch (ReadResponseException.StreamNotFound) {
				yield break;
			}

			yield return enumerator.Current;
		}
	}

	public static Task WriteAsync(this ISystemClient systemClient, string stream, string eventType, byte[] data) {
		var evt = new Event(Guid.NewGuid(), eventType, isJson: true, data);
		var write = new StreamWrite(stream, ExpectedVersion.Any, new LowAllocReadOnlyMemory<Event>(evt));
		return systemClient.Writing.WriteEvents(
			new LowAllocReadOnlyMemory<StreamWrite>(write),
			requireLeader: false,
			principal: SystemAccounts.System);
	}

	public static Task WriteBatchAsync(
		this ISystemClient systemClient,
		IReadOnlyList<PendingEvent> events) {
		if (events.Count == 0)
			return Task.CompletedTask;

		var streamIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
		var streams = LowAllocReadOnlyMemory<string>.Builder.Empty;
		var expectedRevisions = LowAllocReadOnlyMemory<long>.Builder.Empty;
		var eventBuilder = LowAllocReadOnlyMemory<Event>.Builder.Empty;
		var eventStreamIndexes = LowAllocReadOnlyMemory<int>.Builder.Empty;

		foreach (var e in events) {
			if (!streamIndexes.TryGetValue(e.Stream, out var idx)) {
				idx = streams.Count;
				streamIndexes[e.Stream] = idx;
				streams = streams.Add(e.Stream);
				expectedRevisions = expectedRevisions.Add(ExpectedVersion.Any);
			}
			eventBuilder = eventBuilder.Add(new Event(Guid.NewGuid(), e.EventType, isJson: true, e.Data));
			eventStreamIndexes = eventStreamIndexes.Add(idx);
		}

		return systemClient.Writing.WriteEvents(
			streams.Build(),
			expectedRevisions.Build(),
			eventBuilder.Build(),
			eventStreamIndexes.Build(),
			requireLeader: false,
			principal: SystemAccounts.System);
	}
}

public readonly record struct PendingEvent(string Stream, string EventType, byte[] Data);
