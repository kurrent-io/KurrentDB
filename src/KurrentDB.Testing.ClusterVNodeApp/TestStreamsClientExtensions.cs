// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using EventStore.Client;
using EventStore.Client.Streams;
using Grpc.Core;

namespace KurrentDB.Testing;

public static class TestStreamsClientExtensions {
	public static IAsyncEnumerable<EventRecord> ReadAllForwardFiltered(
		this Streams.StreamsClient client,
		string streamPrefixFilter,
		CancellationToken ct) {

		return ReadAllFiltered(client, streamPrefixFilter, forwards: true, ct);
	}

	public static IAsyncEnumerable<EventRecord> ReadAllBackwardFiltered(
		this Streams.StreamsClient client,
		string streamPrefixFilter,
		CancellationToken ct) {

		return ReadAllFiltered(client, streamPrefixFilter, forwards: false, ct);
	}

	public static async IAsyncEnumerable<EventRecord> ReadAllFiltered(
		this Streams.StreamsClient client,
		string streamPrefixFilter,
		bool forwards,
		[EnumeratorCancellation] CancellationToken ct) {

		var request = new ReadReq {
			Options = new() {
				All = forwards
					? new() { Start = new() }
					: new() { End = new() },
				ReadDirection = forwards
					? ReadReq.Types.Options.Types.ReadDirection.Forwards
					: ReadReq.Types.Options.Types.ReadDirection.Backwards,
				ResolveLinks = false,
				Count = ulong.MaxValue,
				Filter = new() {
					StreamIdentifier = new() {
						Prefix = {
							streamPrefixFilter,
						}
					},
					Max = 64,
					CheckpointIntervalMultiplier = 1,
				},
				UuidOption = new() {
					String = new(),
				},
				ControlOption = new() {
					Compatibility = 1,
				},
			},
		};

		using var call = client.Read(request, cancellationToken: ct);

		await foreach (var response in call.ResponseStream.ReadAllAsync(ct)) {
			if (response.Event is { Event: { } } evt) {
				yield return evt.Event.ToEventRecord();
			}
		}
	}

	public static async IAsyncEnumerable<EventRecord> SubscribeToAllFiltered(
		this Streams.StreamsClient client,
		string streamPrefixFilter,
		[EnumeratorCancellation] CancellationToken ct) {

		var request = new ReadReq {
			Options = new() {
				All = new() {
					Start = new(),
				},
				ReadDirection = ReadReq.Types.Options.Types.ReadDirection.Forwards,
				ResolveLinks = false,
				Subscription = new(),
				Filter = new() {
					StreamIdentifier = new() {
						Prefix = {
							streamPrefixFilter,
						}
					},
					Max = 64,
					CheckpointIntervalMultiplier = 1,
				},
				UuidOption = new() {
					String = new(),
				},
				ControlOption = new() {
					Compatibility = 1,
				},
			}
		};

		using var call = client.Read(request, cancellationToken: ct);

		await foreach (var response in call.ResponseStream.ReadAllAsync(ct)) {
			if (response.Event is { Event: { } } evt) {
				yield return evt.Event.ToEventRecord();
			}
		}
	}

	static EventRecord ToEventRecord(this ReadResp.Types.ReadEvent.Types.RecordedEvent readEvent) => new(
		eventStreamId: readEvent.StreamIdentifier.StreamName.ToStringUtf8(),
		eventId: Uuid.Parse(readEvent.Id.String),
		eventNumber: readEvent.StreamRevision,
		position: new Position(readEvent.CommitPosition, readEvent.PreparePosition),
		metadata: readEvent.Metadata,
		data: readEvent.Data.Memory,
		customMetadata: readEvent.CustomMetadata.Memory);
}
