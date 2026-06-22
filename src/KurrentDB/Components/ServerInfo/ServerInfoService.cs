// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using KurrentDB.Components.Shared;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services;

namespace KurrentDB.Components.ServerInfo;

// Stored as a $ServerInfo event in the $server-info stream. The stream is read by Navigator
// and the legacy UI, so the format must remain compatible.
public class ServerInfoData {
	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("production")]
	public bool Production { get; set; }
}

public class ServerInfoService(IPublisher publisher, IAuthorizationProvider authorizer) {
	const string StreamName = "$server-info";
	const string MetadataStreamName = "$$$server-info";
	const string EventType = "$ServerInfo";

	// $server-info is read/written as a normal stream, so authorize the same Streams.Read/Write ops the
	// gRPC/HTTP read+append paths use (the core doesn't re-apply the policy). Writing the metastream maps
	// to metadataWrite on the base stream via LegacyStreamPermissionAssertion, so pass the bare Write op
	// with the metastream id — exactly as StreamsService does for $$ metastreams.
	static Operation ReadOp(string streamId) => UiOperations.ReadStream(streamId);
	static Operation WriteOp(string streamId) => UiOperations.WriteStream(streamId);

	static readonly JsonSerializerOptions JsonOpts = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	// Raised after a successful update so UI (e.g. the app-bar badge) can refresh
	// without waiting for its next poll. Scoped service => same circuit, same instance.
	public event Action Changed;

	public async ValueTask<ServerInfoData> GetAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, ReadOp(StreamName), ct);
		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<ClientMessage.ReadStreamEventsBackwardCompleted>();
		publisher.Publish(new ClientMessage.ReadStreamEventsBackward(
			corrId, corrId, envelope,
			eventStreamId: StreamName,
			fromEventNumber: -1,
			maxCount: 1,
			resolveLinkTos: false,
			requireLeader: false,
			validationStreamVersion: null,
			user: principal,
			replyOnExpired: false));

		var result = await envelope.Task.WaitAsync(ct);
		if (result.Result != ReadStreamResult.Success || result.Events.Count == 0)
			return new ServerInfoData();

		var evt = result.Events[0].Event;
		try {
			return JsonSerializer.Deserialize<ServerInfoData>(evt.Data.Span, JsonOpts) ?? new ServerInfoData();
		} catch {
			return new ServerInfoData();
		}
	}

	public async ValueTask UpdateAsync(ClaimsPrincipal principal, ServerInfoData updates, CancellationToken ct) {
		await Authorize(principal, WriteOp(StreamName), ct);

		// Ensure stream metadata is set (maxCount: 1, $admins write, $all read) before writing.
		// We always write metadata — duplicates are idempotent.
		await EnsureMetadataAsync(principal, ct);

		// Merge with previous data so partial updates preserve other fields.
		var prev = await GetAsync(principal, ct);
		var merged = new ServerInfoData {
			Name = updates.Name ?? prev.Name,
			Production = updates.Production,
		};

		var corrId = Guid.NewGuid();
		// requireLeader: true — like other UI writes, the UI can't replay credentials on a forwarded write
		// (a follower forward re-authenticates as anonymous and is denied), so require the leader up front.
		var envelope = new TcsEnvelope<Message>();
		var evt = new Event(
			Guid.NewGuid(), EventType, isJson: true,
			JsonSerializer.SerializeToUtf8Bytes(merged, JsonOpts));
		publisher.Publish(ClientMessage.WriteEvents.ForSingleEvent(
			corrId, corrId, envelope,
			requireLeader: true,
			StreamName, ExpectedVersion.Any, evt,
			principal));
		var reply = await envelope.Task.WaitAsync(ct);
		switch (reply) {
			case ClientMessage.WriteEventsCompleted { Result: OperationResult.Success }:
				break;
			case ClientMessage.WriteEventsCompleted failed:
				throw new ServerInfoException($"Failed to write server info: {failed.Result}. {failed.Message}");
			case ClientMessage.NotHandled:
				throw NotLeaderException();
			default:
				throw new ServerInfoException($"Unexpected response: {reply.GetType().Name}.");
		}

		Changed?.Invoke();
	}

	async ValueTask EnsureMetadataAsync(ClaimsPrincipal principal, CancellationToken ct) {
		await Authorize(principal, WriteOp(MetadataStreamName), ct);

		var metadata = new StreamMetadata(
			maxCount: 1,
			acl: new StreamAcl(
				readRole: SystemRoles.All,
				writeRole: SystemRoles.Admins,
				deleteRole: SystemRoles.Admins,
				metaReadRole: SystemRoles.All,
				metaWriteRole: SystemRoles.Admins));

		var corrId = Guid.NewGuid();
		var envelope = new TcsEnvelope<Message>();
		var evt = new Event(
			Guid.NewGuid(), "$metadata", isJson: true,
			Encoding.UTF8.GetBytes(metadata.ToJsonString()));
		publisher.Publish(ClientMessage.WriteEvents.ForSingleEvent(
			corrId, corrId, envelope,
			requireLeader: true,
			MetadataStreamName, ExpectedVersion.Any, evt,
			principal));
		var reply = await envelope.Task.WaitAsync(ct);
		switch (reply) {
			case ClientMessage.WriteEventsCompleted { Result: OperationResult.Success }:
				break;
			case ClientMessage.WriteEventsCompleted failed:
				throw new ServerInfoException($"Failed to write metadata: {failed.Result}. {failed.Message}");
			case ClientMessage.NotHandled:
				throw NotLeaderException();
			default:
				throw new ServerInfoException($"Unexpected response: {reply.GetType().Name}.");
		}
	}

	static ServerInfoException NotLeaderException() =>
		new("Server info can only be changed on the leader node.");

	async ValueTask Authorize(ClaimsPrincipal principal, Operation operation, CancellationToken ct) {
		if (!await authorizer.CheckAccessAsync(principal, operation, ct))
			throw new ServerInfoException("Access denied.");
	}
}

public class ServerInfoException(string message) : Exception(message);
