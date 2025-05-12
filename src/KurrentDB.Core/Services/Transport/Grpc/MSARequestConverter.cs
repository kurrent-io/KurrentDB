// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Threading;
using Google.Protobuf;
using Grpc.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Protobuf;
using KurrentDB.Protobuf.Server;
using KurrentDB.Protocol.V2;

namespace KurrentDB.Core.Services.Transport.Grpc;

// Logic for converting MultiStreamAppend gRPC messages to ClientMessages.
public class MSARequestConverter {
	private readonly int _maxAppendSize;
	private readonly int _maxAppendEventSize;

	public MSARequestConverter(int maxAppendSize, int maxAppendEventSize) {
		_maxAppendSize = maxAppendSize;
		_maxAppendEventSize = maxAppendEventSize;
	}

	public ClientMessage.WriteEvents ConvertRequests(
		IReadOnlyList<AppendStreamRequest> appendStreamRequests,
		IEnvelope envelope,
		ClaimsPrincipal user,
		CancellationToken token) {

		if (appendStreamRequests is []) {
			throw new RpcException(new Status(
				StatusCode.InvalidArgument,
				"At least one AppendStreamRequest must be present"));
		}
		// todo: in the future can we rent the memory from a pool?
		var eventStreamIds = ImmutableArray.CreateBuilder<string>(initialCapacity: appendStreamRequests.Count);
		var expectedVersions = ImmutableArray.CreateBuilder<long>(initialCapacity: appendStreamRequests.Count);

		var numEvents = 0;
		foreach (var appendStreamRequest in appendStreamRequests) {
			numEvents += appendStreamRequest.Records.Count;
		}

		var events = ImmutableArray.CreateBuilder<Event>(initialCapacity: numEvents);
		// todo: if all requests are for one stream we can leave this null
		var eventStreamIndexes = ImmutableArray.CreateBuilder<int>(initialCapacity: numEvents);

		var totalSize = 0;
		var seenStreams = new HashSet<string>();

		foreach (var appendStreamRequest in appendStreamRequests) {
			if (!seenStreams.Add(appendStreamRequest.Stream)) {
				// temporary limitation
				throw new RpcException(new Status(
					StatusCode.InvalidArgument,
					"Two AppendStreamRequests for one stream is not currently supported"));
			}

			if (appendStreamRequest.Records is []) {
				throw new RpcException(new Status(
					StatusCode.InvalidArgument,
					$"Write to stream \"{appendStreamRequest.Stream}\" does not have any records"));
			}

			eventStreamIds.Add(appendStreamRequest.Stream);
			expectedVersions.Add(appendStreamRequest.HasExpectedRevision
				? appendStreamRequest.ExpectedRevision
				: ExpectedVersion.Any);

			var eventStreamIndex = eventStreamIds.Count - 1;
			foreach (var appendRecord in appendStreamRequest.Records) {
				eventStreamIndexes.Add(eventStreamIndex);
				var evt = ConvertRecord(appendRecord);

				// todo: consider if these two size related exceptions ought to be non-exceptional
				var eventSize = Event.SizeOnDisk(evt.EventType, evt.Data, evt.Metadata, evt.Properties);
				if (eventSize > _maxAppendEventSize) {
					throw RpcExceptions.MaxAppendEventSizeExceeded(evt.EventId.ToString(), eventSize, _maxAppendEventSize);
				}

				totalSize += eventSize;

				if (totalSize > _maxAppendSize) {
					throw RpcExceptions.MaxAppendSizeExceeded(_maxAppendSize);
				}

				events.Add(evt);
			}
		}

		var correlationId = Guid.NewGuid();

		try {
			var writeEvents = new ClientMessage.WriteEvents(
				internalCorrId: correlationId,
				correlationId: correlationId,
				envelope: envelope,
				requireLeader: true,
				eventStreamIds: eventStreamIds.ToImmutable(),
				expectedVersions: expectedVersions.ToImmutable(),
				events: events.ToImmutable(),
				eventStreamIndexes: eventStreamIndexes.ToImmutable(),
				user: user,
				cancellationToken: token);

			return writeEvents;
		} catch (ArgumentException ex) {
			throw new RpcException(new Status(
				StatusCode.InvalidArgument,
				ex.Message));
		}
	}

	public Event ConvertRecord(AppendRecord appendRecord) {
		var eventTypeString = GetRequiredStringProperty(appendRecord, Constants.Properties.EventType);
		var contentTypeString = GetRequiredStringProperty(appendRecord, Constants.Properties.DataFormat);

		Guid eventId;
		if (appendRecord.HasRecordId) {
			if (!Guid.TryParse(appendRecord.RecordId, out eventId)) {
				throw new RpcException(new Status(
					StatusCode.InvalidArgument,
					$"Could not parse RecordId \"{appendRecord.RecordId}\" to GUID"));
			}
		} else {
			eventId = Guid.NewGuid();
		}

		byte[] metadata = appendRecord.Properties.TryGetValue(
			Constants.Properties.LegacyMetadata,
			out var metadataValue)
			? metadataValue.BytesValue.ToByteArray()
			: [];

		var properties = new Properties();
		foreach (var property in appendRecord.Properties) {
			if (property.Key
				is Constants.Properties.LegacyMetadata
				or Constants.Properties.EventType
				or Constants.Properties.DataFormat)
				continue;

			properties.PropertiesValues.Add(property.Key, property.Value);
		}

		var evt = new Event(
			eventId: eventId,
			eventType: eventTypeString,
			isJson: contentTypeString == Constants.Properties.DataFormats.Json,
			data: appendRecord.Data.ToByteArray(),
			metadata: metadata,
			properties: properties.PropertiesValues.Count == 0 ? [] : properties.ToByteArray());
		return evt;
	}

	private static string GetRequiredStringProperty(AppendRecord appendRecord, string key) =>
		appendRecord.Properties.TryGetValue(key, out var value) &&
		value.KindCase == DynamicValue.KindOneofCase.BytesValue
			? value.BytesValue.ToStringUtf8()
			: throw RpcExceptions.RequiredPropertyMissing(key);
}
