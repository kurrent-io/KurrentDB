// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Eventuous;
using Kurrent.Surge;
using Kurrent.Surge.Producers;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Serializers;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.UserManagement;

namespace KurrentDB.Kontext.Workspaces.ControlPlane;

public sealed class WorkspaceEventStore : IEventStore {
	readonly IEventStore _inner;
	readonly ISystemClient _client;
	readonly Serialize _serialize;

	public WorkspaceEventStore(IEventStore inner, ISystemClient client, SchemaRegistry schemaRegistry) {
		_inner = inner;
		_client = client;
		_serialize = schemaRegistry.Serialize;
	}

	public async Task<AppendEventsResult> AppendEvents(
		StreamName stream,
		ExpectedStreamVersion expectedVersion,
		IReadOnlyCollection<NewStreamEvent> events,
		CancellationToken cancellationToken) {
		var duplicate = stream.ToString().StartsWith(WorkspaceNaming.Category);

		var writes = new LowAllocReadOnlyMemory<StreamWrite>.Builder();
		var first = true;

		foreach (var evt in events) {
			var message = Message.Builder
				.Value(evt.Payload!)
				.Headers(new Headers(evt.Metadata.ToHeaders()))
				.WithSchemaType(SchemaDataFormat.Json)
				.Create();
			var data = (await _serialize(evt.Payload, message.Headers)).ToArray();
			var schema = SchemaInfo.FromHeaders(message.Headers);
			var isJson = schema.SchemaDataFormat == SchemaDataFormat.Json;

			writes = writes.Add(new(
				Stream: stream,
				ExpectedRevision: first ? expectedVersion.Value : ExpectedVersion.Any,
				Events: new(new Event(
					eventId: evt.Id,
					eventType: schema.SchemaName,
					isJson: isJson,
					data: data))));

			if (duplicate) {
				writes = writes.Add(new(
					Stream: WorkspaceNaming.ManagementAllStream,
					ExpectedRevision: ExpectedVersion.Any,
					Events: new(new Event(
						eventId: Guid.NewGuid(),
						eventType: schema.SchemaName,
						isJson: isJson,
						data: data))));
			}

			first = false;
		}

		var (position, streamRevisions) = await _client.Writing.WriteEvents(
			writes.Build(), requireLeader: false, SystemAccounts.System, cancellationToken);

		return new AppendEventsResult(
			GlobalPosition: position.CommitPosition,
			NextExpectedVersion: streamRevisions.Span[0].ToInt64());
	}

	public Task DeleteStream(StreamName stream, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default) =>
		_inner.DeleteStream(stream, expectedVersion, cancellationToken);

	public Task<StreamEvent[]> ReadEvents(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) =>
		_inner.ReadEvents(stream, start, count, cancellationToken);

	public Task<StreamEvent[]> ReadEventsBackwards(StreamName stream, StreamReadPosition start, int count, CancellationToken cancellationToken) =>
		_inner.ReadEventsBackwards(stream, start, count, cancellationToken);

	public Task<bool> StreamExists(StreamName stream, CancellationToken cancellationToken = default) =>
		_inner.StreamExists(stream, cancellationToken);

	public Task TruncateStream(StreamName stream, StreamTruncatePosition truncatePosition, ExpectedStreamVersion expectedVersion, CancellationToken cancellationToken = default) =>
		_inner.TruncateStream(stream, truncatePosition, expectedVersion, cancellationToken);
}