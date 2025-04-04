// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Client;
using EventStore.Core.Services.Transport.Common;
using Google.Protobuf.WellKnownTypes;
using Empty = Google.Protobuf.WellKnownTypes.Empty;

// ReSharper disable once CheckNamespace
namespace Google.Rpc;

partial class Status {
	public static Status WrongExpectedVersion(StreamRevision currentStreamRevision,
		long expectedVersion) => new() {
			Message = nameof(WrongExpectedVersion),
			Details = Any.Pack(EventStore.Client.WrongExpectedVersion.Create(currentStreamRevision, expectedVersion)),
			Code = Code.AlreadyExists
		};

	public static Status StreamDeleted(StreamIdentifier streamIdentifier) => new() {
		Details = Any.Pack(new StreamDeleted {
			StreamIdentifier = streamIdentifier
		}),
		Message = nameof(StreamDeleted),
		Code = Code.NotFound
	};

	public static Status AccessDenied { get; } = new() {
		Details = Any.Pack(new AccessDenied()),
		Message = nameof(AccessDenied),
		Code = Code.PermissionDenied
	};

	public static Status Timeout { get; } = new() {
		Details = Any.Pack(new Timeout()),
		Message = nameof(Timeout),
		Code = Code.DeadlineExceeded
	};

	public static Status InvalidTransaction { get; } = new() {
		Details = Any.Pack(new InvalidTransaction()),
		Message = nameof(InvalidTransaction),
		Code = Code.FailedPrecondition
	};

	public static Status Unknown { get; } = new() {
		Details = Any.Pack(new Unknown()),
		Message = nameof(Unknown),
		Code = Code.Unknown
	};

	public static Status MaximumAppendSizeExceeded(uint maxAppendSize) =>
		new() {
			Details = Any.Pack(new MaximumAppendSizeExceeded {
				MaxAppendSize = maxAppendSize
			}),
			Message = nameof(MaximumAppendSizeExceeded),
			Code = Code.InvalidArgument
		};

	public static Status MaximumAppendEventSizeExceeded(string eventId, uint proposedEventSize, uint maxAppendEventSize) =>
		new() {
			Details = Any.Pack(new MaximumAppendEventSizeExceeded {
				EventId = eventId,
				ProposedEventSize = proposedEventSize,
				MaxAppendEventSize = maxAppendEventSize
			}),
			Message = nameof(MaximumAppendEventSizeExceeded),
			Code = Code.InvalidArgument
		};

	public static Status BadRequest(string message) => new() {
		Details = Any.Pack(new BadRequest { Message = message }),
		Message = nameof(BadRequest),
		Code = Code.InvalidArgument
	};

	public static Status InternalError(string message) => new() {
		Details = Any.Pack(new Empty()),
		Message = message,
		Code = Code.Internal
	};
}
