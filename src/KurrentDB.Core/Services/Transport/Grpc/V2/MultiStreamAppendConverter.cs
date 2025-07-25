// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Collections;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc.V2.Utils;
using KurrentDB.Protobuf;
using KurrentDB.Protobuf.Server;
using KurrentDB.Protocol.V2;
using static System.StringComparison;

namespace KurrentDB.Core.Services.Transport.Grpc.V2;

public record ConvertedEvents(
	ImmutableArray<Event> Events,
	ImmutableArray<string> StreamIds,
	ImmutableArray<long> ExpectedVersions,
	ImmutableArray<int> StreamIndexes
) {
	public static readonly ConvertedEvents None = new([], [], [], []);
}

/// <summary>
/// Handles the conversion logic between gRPC AppendStream requests and responses,
/// and the backend messaging protocols for multi-stream append operations.
/// </summary>
/// <param name="chunkSize">
/// Specifies the maximum size, in bytes, of a transaction chunk during processing.
/// </param>
/// <param name="maxAppendSize">
/// Defines the upper limit for the total size of multiple append operations in a single request.
/// </param>
/// <param name="maxAppendEventSize">
/// Indicates the maximum allowable size, in bytes, for a single event being appended.
/// </param>
public class MultiStreamAppendConverter(int chunkSize, int maxAppendSize, int maxAppendEventSize) {
	static readonly ErrorDetails.Types.StreamDeleted StreamDeletedError = new();

	public ConvertedEvents ConvertToEvents(IReadOnlyList<AppendStreamRequest> requests) {
		if (requests.Count == 0)
			throw RpcExceptions.InvalidArgument("At least one AppendStreamRequest must be present");

		// todo: in the future can we rent the memory from a pool?
		var streamIds = ImmutableArray.CreateBuilder<string>(initialCapacity: requests.Count);
		var expectedVersions = ImmutableArray.CreateBuilder<long>(initialCapacity: requests.Count);

		var numEvents = requests.Sum(x => x.Records.Count);

		var events = ImmutableArray.CreateBuilder<Event>(initialCapacity: numEvents);
		// todo: if all requests are for one stream we can leave this null
		var streamIndexes = ImmutableArray.CreateBuilder<int>(initialCapacity: numEvents);

		var totalSize = 0;
		foreach (var appendStreamRequest in requests) {
			streamIds.Add(appendStreamRequest.Stream);
			expectedVersions.Add(appendStreamRequest.HasExpectedRevision
				? appendStreamRequest.ExpectedRevision
				: ExpectedVersion.Any);

			var eventStreamIndex = streamIds.Count - 1;
			foreach (var appendRecord in appendStreamRequest.Records) {
				streamIndexes.Add(eventStreamIndex);
				var evt = ConvertToEvent(appendRecord);

				// todo: consider if these two size related exceptions ought to be non-exceptional
				var eventSize = Event.SizeOnDisk(evt.EventType, evt.Data, evt.Metadata, evt.Properties);
				if (eventSize > maxAppendEventSize)
					throw RpcExceptions.MaxAppendEventSizeExceeded(evt.EventId.ToString(), eventSize, maxAppendEventSize);

				totalSize += eventSize;
				if (totalSize > maxAppendSize)
					throw RpcExceptions.MaxAppendSizeExceeded(maxAppendSize);

				events.Add(evt);
			}
		}

		return new(
			events.ToImmutable(),
			streamIds.ToImmutable(),
			expectedVersions.ToImmutable(),
			streamIndexes.ToImmutable()
		);
	}

	public MultiStreamAppendResponse ConvertToResponse(Message message, IReadOnlyList<AppendStreamRequest> requests) {
		return message switch {
			ClientMessage.NotHandled msg when RpcExceptions.TryHandleNotHandled(msg, out var ex) => throw ex,
			not ClientMessage.WriteEventsCompleted => throw RpcExceptions.UnknownMessage<ClientMessage.WriteEventsCompleted>(message),

			ClientMessage.WriteEventsCompleted { Result: OperationResult.Success } msg              => OnSuccess(msg, requests),
			ClientMessage.WriteEventsCompleted { Result: OperationResult.WrongExpectedVersion } msg => OnWrongExpectedVersion(msg, requests),
			ClientMessage.WriteEventsCompleted { Result: OperationResult.StreamDeleted } msg        => OnStreamDeleted(msg, requests),
			ClientMessage.WriteEventsCompleted { Result: OperationResult.AccessDenied }             => OnAccessDenied(),
			ClientMessage.WriteEventsCompleted { Result: OperationResult.InvalidTransaction }       => OnInvalidTransaction(chunkSize),

			ClientMessage.WriteEventsCompleted { Result:
				OperationResult.PrepareTimeout or
				OperationResult.CommitTimeout or
				OperationResult.ForwardTimeout
			} msg => throw RpcExceptions.Timeout(msg.Message),

			ClientMessage.WriteEventsCompleted msg => throw RpcExceptions.UnknownError(msg.Result)
		};

		static MultiStreamAppendResponse OnSuccess(ClientMessage.WriteEventsCompleted completed, IReadOnlyList<AppendStreamRequest> requests) {
			Debug.Assert(completed.LastEventNumbers.Length == requests.Count,
				$"LastEventNumbers length {completed.LastEventNumbers.Length} does not match requests count {requests.Count}");

			Debug.Assert(completed.CommitPosition >= 0,
				$"Commit position should never be negative: {completed.CommitPosition}");

			var response = new MultiStreamAppendResponse { Success = new() };

			for (var i = 0; i < completed.LastEventNumbers.Length; i++) {
				response.Success.Output.Add(new AppendStreamSuccess {
					Stream         = requests[i].Stream,
					StreamRevision = completed.LastEventNumbers.Span[i],
					Position       = completed.CommitPosition, // Position for the transaction - which is the same for every stream
				});
			}

			return response;
		}

		static MultiStreamAppendResponse OnWrongExpectedVersion(ClientMessage.WriteEventsCompleted completed, IReadOnlyList<AppendStreamRequest> requests) {
			// wrong expected version MultiStreamAppendResponse one AppendStreamFailure per failure.
			Debug.Assert(completed.FailureCurrentVersions.Length == completed.FailureStreamIndexes.Length,
				$"FailureCurrentVersions length {completed.FailureCurrentVersions.Length} does not match FailureStreamIndexes length {completed.FailureStreamIndexes.Length}");

			var response = new MultiStreamAppendResponse { Failure = new() };

			for (var i = 0; i < completed.FailureCurrentVersions.Length; i++) {
				response.Failure.Output.Add(new AppendStreamFailure {
					Stream = requests[completed.FailureStreamIndexes.Span[i]].Stream,
					StreamRevisionConflict = new() {
						StreamRevision = completed.FailureCurrentVersions.Span[i],
					},
				});
			}

			return response;
		}

		static MultiStreamAppendResponse OnStreamDeleted(ClientMessage.WriteEventsCompleted completed, IReadOnlyList<AppendStreamRequest> requests) {
			var failure = new AppendStreamFailure {
				Stream        = completed.FailureStreamIndexes.Span is [var streamIndex, ..] ? requests[streamIndex].Stream : "<unknown>",
				StreamDeleted = StreamDeletedError,
			};
			return new() { Failure = new() { Output = { failure } } };
		}

		// this is not a possible case on the gRPC paths, but we handle it anyway
		static MultiStreamAppendResponse OnAccessDenied() {
			var failure = new AppendStreamFailure {
				Stream       = "<unknown>",
				AccessDenied = new()
			};
			return new() { Failure = new() { Output = { failure }, }, };
		}

		static MultiStreamAppendResponse OnInvalidTransaction(int chunkSize) {
			var failure = new AppendStreamFailure {
				TransactionMaxSizeExceeded = new() { MaxSize = chunkSize, },
			};
			return new() { Failure = new() { Output = { failure } } };
		}
	}

	public static Event ConvertToEvent(AppendRecord appendRecord) {
		var recordId = GetRecordId(appendRecord);
		var schemaName = GetSchemaName(appendRecord.Properties);
		var schemaDataFormat = GetSchemaDataFormat(appendRecord.Properties);

		var isJson = schemaDataFormat
			.Equals(Constants.Properties.DataFormats.Json, OrdinalIgnoreCase);

		var isBytes = schemaDataFormat
			.Equals(Constants.Properties.DataFormats.Bytes, OrdinalIgnoreCase);

		// remove the properties that are not needed
		appendRecord.Properties.Remove(Constants.Properties.EventTypeKey);

		if (isJson || isBytes)
			appendRecord.Properties.Remove(Constants.Properties.DataFormatKey);

		var properties = appendRecord.Properties.Count > 0
			? new Properties { PropertiesValues = { appendRecord.Properties } }.ToByteArray()
			: [];

		return new(
			eventId: recordId,
			eventType: schemaName,
			isJson: isJson,
			data: appendRecord.Data.ToByteArray(),
			metadata: [],
			properties: properties
		);

		static Guid GetRecordId(AppendRecord appendRecord) {
			return appendRecord.HasRecordId
				? !Guid.TryParse(appendRecord.RecordId, out var id)
					? throw RpcExceptions.InvalidArgument($"Could not parse RecordId '{appendRecord.RecordId}' to GUID")
					: id
				: Guid.NewGuid();
		}

		static string GetSchemaName(MapField<string, DynamicValue> source) =>
			source.TryGetValue<string>(Constants.Properties.EventTypeKey, out var value) && !string.IsNullOrWhiteSpace(value)
				? value : throw RpcExceptions.RequiredPropertyMissing(Constants.Properties.EventTypeKey);

		static string GetSchemaDataFormat(MapField<string, DynamicValue> source) =>
			source.TryGetValue<string>(Constants.Properties.DataFormatKey, out var value) && !string.IsNullOrWhiteSpace(value)
				? value : throw RpcExceptions.RequiredPropertyMissing(Constants.Properties.DataFormatKey);
	}
}
