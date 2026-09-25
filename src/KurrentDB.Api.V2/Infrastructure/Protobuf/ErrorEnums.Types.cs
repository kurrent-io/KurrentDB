// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Google.Protobuf.Reflection;
using Kurrent.Rpc;
using KurrentDB.Protocol.V2.Indexes.Errors;
using KurrentDB.Protocol.V2.Streams.Errors;

namespace KurrentDB.Api.Infrastructure.Protobuf;

partial struct ErrorEnums
	: IErrorEnum<ServerError>,
		IErrorEnum<IndexesError>,
		IErrorEnum<StreamsError> {
	static EnumDescriptor IErrorEnum<ServerError>.Descriptor
		=> Kurrent.Rpc.ErrorsReflection.Descriptor.FindTypeByName<EnumDescriptor>(nameof(ServerError));

	static Type? IErrorEnum<ServerError>.GetDetailsType(ServerError value) => value switch {
		ServerError.AccessDenied => typeof(AccessDeniedErrorDetails),
		ServerError.NotLeaderNode => typeof(NotLeaderNodeErrorDetails),
		ServerError.BadRequest => typeof(Google.Rpc.BadRequest),
		_ => null,
	};

	static EnumDescriptor IErrorEnum<IndexesError>.Descriptor
		=> Protocol.V2.Indexes.Errors.ErrorsReflection.Descriptor.FindTypeByName<EnumDescriptor>(
			nameof(IndexesError));

	static Type? IErrorEnum<IndexesError>.GetDetailsType(IndexesError value)
		=> value switch {
			IndexesError.IndexNotFound => typeof(IndexNotFoundErrorDetails),
			IndexesError.IndexAlreadyExists => typeof(IndexAlreadyExistsErrorDetails),
			IndexesError.IndexesNotReady => typeof(IndexesNotReadyErrorDetails),
			_ => null,
		};

	static EnumDescriptor IErrorEnum<StreamsError>.Descriptor
		=> Protocol.V2.Streams.Errors.ErrorsReflection.Descriptor.FindTypeByName<EnumDescriptor>(
			nameof(StreamsError));

	static Type? IErrorEnum<StreamsError>.GetDetailsType(StreamsError value)
		=> value switch {
			StreamsError.StreamNotFound => typeof(StreamNotFoundErrorDetails),
			StreamsError.StreamAlreadyExists => typeof(StreamAlreadyExistsErrorDetails),
			StreamsError.StreamDeleted => typeof(StreamDeletedErrorDetails),
			StreamsError.StreamTombstoned => typeof(StreamTombstonedErrorDetails),
			StreamsError.StreamRevisionConflict => typeof(StreamRevisionConflictErrorDetails),
			StreamsError.AppendRecordSizeExceeded => typeof(AppendRecordSizeExceededErrorDetails),
			StreamsError.AppendTransactionSizeExceeded => typeof(AppendTransactionSizeExceededErrorDetails),
			StreamsError.StreamAlreadyInAppendSession => typeof(StreamAlreadyInAppendSessionErrorDetails),
			StreamsError.AppendConsistencyViolation => typeof(AppendConsistencyViolationErrorDetails),
			_ => null,
		};
}
