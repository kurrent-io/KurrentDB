// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.Diagnostics;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Infrastructure.Errors;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams.Errors;

namespace KurrentDB.Api.Errors;

public static partial class ApiErrors {
	public static RpcException StreamNotFound(string stream) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");

		var message = $"Stream '{stream}' was not found.";
		var details = new StreamNotFoundErrorDetails { Stream = stream };

		return RpcExceptions.FromError(StreamsError.StreamNotFound, message, details);
	}

	public static RpcException StreamAlreadyExists(string stream) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");

		var message = $"Stream '{stream}' already exists.";
		var details = new StreamAlreadyExistsErrorDetails { Stream = stream };
		return RpcExceptions.FromError(StreamsError.StreamAlreadyExists, message, details);
	}

	public static RpcException StreamDeleted(string stream) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");

		var message = $"Stream '{stream}' has been soft deleted. "
		            + $"It will not be visible in the stream list, "
		            + $"until it is restored by appending to it again.";

		var details = new StreamDeletedErrorDetails { Stream = stream };

		return RpcExceptions.FromError(StreamsError.StreamDeleted, message, details);
	}

	public static RpcException StreamTombstoned(string stream) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");

		var message = $"Stream '{stream}' has been tombstoned. "
		            + $"It has been permanently removed from the system and cannot be restored.";

		var details = new StreamTombstonedErrorDetails { Stream = stream };

		return RpcExceptions.FromError(StreamsError.StreamTombstoned, message, details);
	}

	public static RpcException StreamRevisionConflict(string stream, long expectedRevision, long actualRevision) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");
		Debug.Assert(expectedRevision >= 0, "The expectedRevision must be non-negative!");
		Debug.Assert(actualRevision >= 0, "The actualRevision must be non-negative!");

		var message = $"Append failed due to a revision conflict on stream '{stream}'. " +
		              $"Expected revision: {expectedRevision}. Actual revision: {actualRevision}.";

		var details = new StreamRevisionConflictErrorDetails {
			Stream           = stream,
			ExpectedRevision = expectedRevision,
			ActualRevision   = actualRevision
		};

		return RpcExceptions.FromError(StreamsError.StreamRevisionConflict, message, details);
	}

	public static RpcException StreamAlreadyInAppendSession(string stream) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");

		var message = $"Stream '{stream}' is already part of this append session. " +
		              $"Appending to the same stream multiple times is currently not supported.";

        //KurrentDB.Protocol.V2.Streams.Errors.StreamsError.StreamAlreadyInAppendSessionErrorDetails
		var details = new StreamAlreadyInAppendSessionErrorDetails { Stream = stream };

		return RpcExceptions.FromError(StreamsError.StreamAlreadyInAppendSession, message, details);
	}

	public static RpcException AppendRecordSizeExceeded(string stream, string recordId, int recordSize, int maxSize) {
		Debug.Assert(!string.IsNullOrWhiteSpace(stream), "The stream cannot be empty!");
		Debug.Assert(!string.IsNullOrWhiteSpace(recordId), "The record ID cannot be empty!");
		Debug.Assert(recordSize > 0, "The record size must be positive!");
		Debug.Assert(recordSize >= maxSize, "The record size must be greater than or equal to the max size!");

		var exceededBy = recordSize - maxSize;

		var message = $"The size of record {recordId} ({recordSize.Bytes().Humanize("0.00")}) exceeds the maximum allowed size of "
		            + $"{maxSize.Bytes().Humanize("0.00")} bytes by {exceededBy.Bytes().Humanize("0.00")}";

		var details = new AppendRecordSizeExceededErrorDetails {
			Stream   = stream,
			RecordId = recordId,
			Size     = recordSize,
			MaxSize  = maxSize
		};

		return RpcExceptions.FromError(StreamsError.AppendRecordSizeExceeded, message, details);
	}

	public static RpcException AppendTransactionSizeExceeded(int size, int maxSize = TFConsts.ChunkSize) {
		Debug.Assert(size > 0, "The size must be positive!");
		Debug.Assert(maxSize > 0, "The max size must be positive!");
		Debug.Assert(size > maxSize, "The size must be greater than the max size!");

		var exceededBy = size - maxSize;

		var message = $"The total size of the append transaction ({size.Bytes().Humanize("0.00")}) exceeds the maximum allowed size of "
		            + $"{maxSize.Bytes().Humanize("0.00")} bytes by {exceededBy.Bytes().Humanize("0.00")}";

		var details = new AppendTransactionSizeExceededErrorDetails {
			Size	= size,
			MaxSize = maxSize
		};

		return RpcExceptions.FromError(StreamsError.AppendTransactionSizeExceeded, message, details);
	}
}
