// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;

namespace KurrentDB.Components.Streams;

public class StreamsService(IPublisher publisher, IAuthorizationProvider authorizer) {
	// $all authorizes as a stream read of the "$all" resource (matching the gRPC read path). Reads/writes of
	// a "$$<stream>" metastream are translated by the engine's permission assertion into metadataRead/
	// metadataWrite on the base stream, so we just pass Streams.Read/Write with the actual stream id.
	static Operation ReadOp(string streamId) => UiOperations.ReadStream(streamId);
	static Operation WriteOp(string streamId) => UiOperations.WriteStream(streamId);
	static Operation DeleteOp(string streamId) => UiOperations.DeleteStream(streamId);

	public async ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadStreamForwardAsync(
		ClaimsPrincipal principal, string streamId, long fromEventNumber, int maxCount, CancellationToken ct) {

		await Authorize(principal, ReadOp(streamId), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsForwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsForward(
			corrId, corrId, envelope,
			streamId, fromEventNumber, maxCount,
			resolveLinkTos: true, requireLeader: false,
			validationStreamVersion: null, user: principal,
			replyOnExpired: false));
		return await envelope.Task.WaitAsync(ct);
	}

	public async ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadStreamBackwardAsync(
		ClaimsPrincipal principal, string streamId, long fromEventNumber, int maxCount, CancellationToken ct,
		bool resolveLinkTos = true) {

		await Authorize(principal, ReadOp(streamId), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsBackward(
			corrId, corrId, envelope,
			streamId, fromEventNumber, maxCount,
			resolveLinkTos: resolveLinkTos, requireLeader: false,
			validationStreamVersion: null, user: principal,
			replyOnExpired: false));
		return await envelope.Task.WaitAsync(ct);
	}

	public async ValueTask<ClientMessage.ReadAllEventsBackwardCompleted> ReadAllBackwardAsync(
		ClaimsPrincipal principal, long commitPosition, long preparePosition, int maxCount, CancellationToken ct,
		bool resolveLinkTos = true) {

		await Authorize(principal, ReadOp(SystemStreams.AllStream), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadAllEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadAllEventsBackward(
			corrId, corrId, envelope,
			commitPosition, preparePosition, maxCount,
			resolveLinkTos: resolveLinkTos, requireLeader: false,
			validationTfLastCommitPosition: null, user: principal,
			replyOnExpired: false));
		return await envelope.Task.WaitAsync(ct);
	}

	public async ValueTask<ClientMessage.ReadAllEventsForwardCompleted> ReadAllForwardAsync(
		ClaimsPrincipal principal, long commitPosition, long preparePosition, int maxCount, CancellationToken ct,
		bool resolveLinkTos = true) {

		await Authorize(principal, ReadOp(SystemStreams.AllStream), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadAllEventsForwardCompleted>();
		publisher.Publish(new ClientMessage.ReadAllEventsForward(
			corrId, corrId, envelope,
			commitPosition, preparePosition, maxCount,
			resolveLinkTos: resolveLinkTos, requireLeader: false,
			validationTfLastCommitPosition: null, user: principal,
			replyOnExpired: false));
		return await envelope.Task.WaitAsync(ct);
	}

	public async ValueTask<ClientMessage.ReadEventCompleted> ReadEventAsync(
		ClaimsPrincipal principal, string streamId, long eventNumber, CancellationToken ct) {

		await Authorize(principal, ReadOp(streamId), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadEventCompleted>();
		publisher.Publish(new ClientMessage.ReadEvent(
			corrId, corrId, envelope,
			streamId, eventNumber,
			resolveLinkTos: true, requireLeader: false,
			user: principal));
		return await envelope.Task.WaitAsync(ct);
	}

	// Returns the event number written.
	public async ValueTask<long> WriteEventAsync(
		ClaimsPrincipal principal, string streamId, string eventType,
		string data, string metadata, CancellationToken ct) {

		await Authorize(principal, WriteOp(streamId), ct);
		var corrId = Guid.NewGuid();
		// requireLeader: true — the UI has no way to replay credentials on a forwarded write, so a follower
		// forward would re-authenticate as anonymous and be denied. Instead we require the leader up front;
		// a follower replies NotHandled, which the UI translates into a "write on the leader" message.
		var envelope = new TcsEnvelope<Message>();
		var evt = new Event(Guid.NewGuid(), eventType, isJson: true, data, metadata ?? "{}");
		publisher.Publish(ClientMessage.WriteEvents.ForSingleEvent(
			corrId, corrId, envelope,
			requireLeader: true,
			streamId, ExpectedVersion.Any, evt,
			principal));
		var reply = await envelope.Task.WaitAsync(ct);
		return reply switch {
			ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } ok =>
				ok.LastEventNumbers.Length > 0 ? ok.LastEventNumbers.Span[0] : -1,
			ClientMessage.WriteEventsCompleted failed =>
				throw new StreamsException($"Write failed: {failed.Result}. {failed.Message}"),
			ClientMessage.NotHandled => throw NotLeaderException(),
			_ => throw new StreamsException($"Unexpected response: {reply.GetType().Name}."),
		};
	}

	public async ValueTask DeleteStreamAsync(
		ClaimsPrincipal principal, string streamId, CancellationToken ct) {

		await Authorize(principal, DeleteOp(streamId), ct);
		var corrId = Guid.NewGuid();
		// requireLeader: true for the same reason as WriteEventAsync — the UI can't forward its identity.
		var envelope = new TcsEnvelope<Message>();
		publisher.Publish(new ClientMessage.DeleteStream(
			corrId, corrId, envelope,
			requireLeader: true,
			streamId, ExpectedVersion.Any, hardDelete: false,
			user: principal));
		var reply = await envelope.Task.WaitAsync(ct);
		switch (reply) {
			case ClientMessage.DeleteStreamCompleted { Result: OperationResult.Success }:
				return;
			case ClientMessage.DeleteStreamCompleted failed:
				throw new StreamsException($"Delete failed: {failed.Result}. {failed.Message}");
			case ClientMessage.NotHandled:
				throw NotLeaderException();
			default:
				throw new StreamsException($"Unexpected response: {reply.GetType().Name}.");
		}
	}

	public async ValueTask<StreamMetadata> GetMetadataAsync(
		ClaimsPrincipal principal, string streamId, CancellationToken ct) {

		// Authorized by ReadEventAsync (a read of the "$$<stream>" metastream -> metadataRead on the stream).
		var metaStreamId = SystemStreams.MetastreamOf(streamId);
		var result = await ReadEventAsync(principal, metaStreamId, -1, ct);
		if (result.Result != ReadEventResult.Success || result.Record.Event == null)
			return StreamMetadata.Empty;
		return StreamMetadata.FromJsonBytes(result.Record.Event.Data.Span);
	}

	public async ValueTask UpdateAclAsync(
		ClaimsPrincipal principal, string streamId, StreamAcl acl, CancellationToken ct) {

		var metaStreamId = SystemStreams.MetastreamOf(streamId);
		await Authorize(principal, WriteOp(metaStreamId), ct);   // write to the metastream -> metadataWrite
		var metadata = new StreamMetadata(acl: acl);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		var evt = new Event(Guid.NewGuid(), "$metadata", isJson: true,
			Encoding.UTF8.GetBytes(metadata.ToJsonString()));
		publisher.Publish(ClientMessage.WriteEvents.ForSingleEvent(
			corrId, corrId, envelope,
			requireLeader: true,
			metaStreamId, ExpectedVersion.Any, evt,
			principal));
		var reply = await envelope.Task.WaitAsync(ct);
		switch (reply) {
			case ClientMessage.WriteEventsCompleted { Result: OperationResult.Success }:
				return;
			case ClientMessage.WriteEventsCompleted failed:
				throw new StreamsException($"ACL update failed: {failed.Result}. {failed.Message}");
			case ClientMessage.NotHandled:
				throw NotLeaderException();
			default:
				throw new StreamsException($"Unexpected response: {reply.GetType().Name}.");
		}
	}

	static StreamsException NotLeaderException() =>
		new("This operation must be performed on the leader node. Open the stream browser on the leader.");

	// Live subscription via the catch-up + live enumerator (Enumerator.StreamSubscription / AllSubscription —
	// the same primitive the gRPC read path uses). Streams events appearing after the current end to onEvent,
	// and transparently resubscribes — catching up the gap from the last event seen — if the server drops the
	// subscription, so a transient drop no longer silently freezes live updates. Runs until ct is cancelled.
	// onEvent runs on a background thread; the caller marshals to the UI and de-dupes (a reconnect may
	// re-deliver recent events). Authorization is awaited once up front so a denial surfaces to the caller
	// before the loop starts; lost access mid-stream just ends the subscription quietly (live is best-effort).
	public async Task SubscribeAsync(
		ClaimsPrincipal principal, string streamId,
		Action<ResolvedEvent> onEvent, bool resolveLinkTos, CancellationToken ct) {

		await Authorize(principal, ReadOp(streamId), ct);
		if (streamId == SystemStreams.AllStream)
			await SubscribeToAllLoop(principal, onEvent, resolveLinkTos, ct);
		else
			await SubscribeToStreamLoop(principal, streamId, onEvent, resolveLinkTos, ct);
	}

	async Task SubscribeToStreamLoop(
		ClaimsPrincipal principal, string streamId,
		Action<ResolvedEvent> onEvent, bool resolveLinkTos, CancellationToken ct) {

		// Start live from the end; after a drop, resume from the last event seen so the gap is caught up.
		StreamRevision? checkpoint = StreamRevision.End;
		while (!ct.IsCancellationRequested) {
			try {
				await using var sub = new Enumerator.StreamSubscription<string>(
					bus: publisher, expiryStrategy: DefaultExpiryStrategy.Instance,
					streamName: streamId, checkpoint: checkpoint, resolveLinks: resolveLinkTos,
					user: principal, requiresLeader: false, cancellationToken: ct);
				while (await sub.MoveNextAsync()) {
					if (sub.Current is ReadResponse.EventReceived received) {
						checkpoint = StreamRevision.FromInt64(received.Event.OriginalEventNumber);
						onEvent(received.Event);
					}
				}
			} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
				return;
			} catch (ReadResponseException.AccessDenied) {
				return; // lost permission to read the stream — stop quietly
			} catch (ReadResponseException.StreamDeleted) {
				return; // stream gone — nothing more to deliver
			} catch {
				// transient drop — fall through to a short delay, then resubscribe from the last checkpoint
			}
			if (!await DelayBeforeResubscribe(ct))
				return;
		}
	}

	async Task SubscribeToAllLoop(
		ClaimsPrincipal principal,
		Action<ResolvedEvent> onEvent, bool resolveLinkTos, CancellationToken ct) {

		Position? checkpoint = Position.End;
		while (!ct.IsCancellationRequested) {
			try {
				await using var sub = new Enumerator.AllSubscription(
					bus: publisher, expiryStrategy: DefaultExpiryStrategy.Instance,
					checkpoint: checkpoint, resolveLinks: resolveLinkTos,
					user: principal, requiresLeader: false, cancellationToken: ct);
				while (await sub.MoveNextAsync()) {
					if (sub.Current is ReadResponse.EventReceived received) {
						if (received.Event.OriginalPosition is { } pos)
							checkpoint = new Position((ulong)pos.CommitPosition, (ulong)pos.PreparePosition);
						onEvent(received.Event);
					}
				}
			} catch (OperationCanceledException) when (ct.IsCancellationRequested) {
				return;
			} catch (ReadResponseException.AccessDenied) {
				return;
			} catch {
				// transient drop — resubscribe from the last position
			}
			if (!await DelayBeforeResubscribe(ct))
				return;
		}
	}

	// Brief pause between a dropped subscription and resubscribing. Returns false if cancelled while waiting.
	static async Task<bool> DelayBeforeResubscribe(CancellationToken ct) {
		try {
			await Task.Delay(TimeSpan.FromSeconds(2), ct);
			return true;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	// Enforce the configured authorization policy before publishing, translating a denial into the
	// StreamsException the components already handle.
	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new StreamsException("Access denied.");
	}
}

public class StreamsException(string message) : Exception(message);
