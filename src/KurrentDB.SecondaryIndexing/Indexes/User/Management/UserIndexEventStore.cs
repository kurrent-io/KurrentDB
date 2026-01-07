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

namespace KurrentDB.SecondaryIndexing.Indexes.User.Management;

// This spots all UserIndex management events and writes them to the index all stream for the
// user index engine to consume.
// In the future the UserIndexEngineSubscription may be driven by a default secondary index
// when we have one that captures system events, and then this class will not be necessary.
// We are reluctant to add a special custom index for this because it would require a log
// scan to initialize and add a extra $all subscription even if custom indexes are not otherwise in use.
// Somewhat similar to KurrentDB.Surge.Eventuous.SystemEventStore
public class UserIndexEventStore : IEventStore {
	readonly IEventStore _inner;
	readonly ISystemClient _client;
	readonly Serialize _serialize;

	public UserIndexEventStore(
		IEventStore inner,
		ISystemClient client,
		SchemaRegistry schemaRegistry) {
		
		_serialize = schemaRegistry.Serialize;
		_client = client;
		_inner = inner;
	}

	public async Task<AppendEventsResult> AppendEvents(
		StreamName stream,
		ExpectedStreamVersion expectedVersion,
		IReadOnlyCollection<NewStreamEvent> events,
		CancellationToken cancellationToken) {

		// decide if we are going to duplicate the events to the user index all Stream
		var duplicate = stream.ToString().StartsWith(UserIndexConstants.Category);

		LowAllocReadOnlyMemory<string> streams = duplicate
			? [stream, UserIndexConstants.ManagementAllStream]
			: new(stream);

		LowAllocReadOnlyMemory<long> expectedVersions = duplicate
			? [expectedVersion.Value, ExpectedVersion.Any]
			: new(expectedVersion.Value);

		var totalEventCount = duplicate ? events.Count * 2 : events.Count;
		var processedEvents = new Event[totalEventCount];
		var eventStreamIndexes = new int[totalEventCount];

		var originalStreamIndex = 0;
		var allStreamIndex = 1;

		int i = 0;
		foreach (var evt in events) {
			var message = Message.Builder
				.Value(evt.Payload!)
				.Headers(new Headers(evt.Metadata.ToHeaders()))
				.WithSchemaType(SchemaDataFormat.Json)
				.Create();
			var data = await _serialize(evt.Payload, message.Headers);
			var dataArray = data.ToArray();
			var schema = SchemaInfo.FromHeaders(message.Headers);
			var isJson = schema.SchemaDataFormat == SchemaDataFormat.Json;

			// process the events into the original stream
			processedEvents[i] = new Event(
				eventId: evt.Id,
				eventType: schema.SchemaName,
				isJson: isJson,
				data: dataArray);
			eventStreamIndexes[i] = originalStreamIndex;
			i++;

			// process the events into the management stream if necessary
			if (duplicate) {
				processedEvents[i] = new Event(
					eventId: Guid.NewGuid(),
					eventType: schema.SchemaName,
					isJson: isJson,
					data: dataArray);
				eventStreamIndexes[i] = allStreamIndex;
				i++;
			}
		}

		// write all the events transactionally
		var (position, streamRevisions) = await _client.Writing.WriteEvents(
			streams: streams,
			expectedRevisions: expectedVersions,
			events: processedEvents,
			eventStreamIndexes: eventStreamIndexes,
			cancellationToken);

		return new AppendEventsResult(
			GlobalPosition: position.CommitPosition,
			NextExpectedVersion: streamRevisions.Span[originalStreamIndex].ToInt64());
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
