// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
#pragma warning disable CS8321 // Local function declared but never used

using System.Diagnostics;
using System.Text;
using Grpc.Core;
using Humanizer;
using KurrentDB.Api.Infrastructure.Errors;
using KurrentDB.Api.Streams;
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
		// Debug.Assert(expectedRevision >= 0, "The expectedRevision must be non-negative!");
		// Debug.Assert(actualRevision >= 0, "The actualRevision must be non-negative!");

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

		var message = $"Stream '{stream}' already has a different group of messages in this session. " +
		              $"Appends for the same stream must currently be grouped together and not interleaved with appends for other streams.";

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

		var message = $"The size of record {recordId} ({recordSize.Bytes().Humanize("0.000")}) exceeds the maximum allowed size of "
		            + $"{maxSize.Bytes().Humanize("0.000")} bytes by {exceededBy.Bytes().Humanize("0.000")}";

		var details = new AppendRecordSizeExceededErrorDetails {
			Stream   = stream,
			RecordId = recordId,
			Size     = recordSize,
			MaxSize  = maxSize
		};

		return RpcExceptions.FromError(StreamsError.AppendRecordSizeExceeded, message, details);
	}

	public static RpcException AppendTransactionSizeExceeded(int records, int size, int maxSize) {
		Debug.Assert(size > 0, "The size must be positive!");
		Debug.Assert(maxSize > 0, "The max size must be positive!");
		Debug.Assert(size > maxSize, "The size must be greater than the max size!");

		var exceededBy = size - maxSize;

        var message = $"Transaction size ({size.Bytes().Humanize("0.000")}) exceeded the maximum allowed size of "
                    + $"{maxSize.Bytes().Humanize("0.000")} by {exceededBy.Bytes().Humanize("0.000")}, after {records} record(s).";

		var details = new AppendTransactionSizeExceededErrorDetails {
			Size	= size,
			MaxSize = maxSize
		};

		return RpcExceptions.FromError(StreamsError.AppendTransactionSizeExceeded, message, details);
	}

    public static RpcException AppendTransactionNoRequests() {
        const string message = "Append session started, but no append requests were sent before ending the session.";
        return RpcExceptions.FromError(StreamsError.AppendSessionNoRequests, message);
    }

	public static RpcException AppendConsistencyViolation(List<ConsistencyViolation> violations) {
		// ----------------------------------------------------------------------------------
		// Consistency check result matrix
		// ----------------------------------------------------------------------------------
		//
		// +--------------------+------------+-----------+---------------+--------------+
		// | Expected \ Actual  | Rev M (≥0) | NO_STREAM | DELETED       | TOMBSTONED   |
		// +--------------------+------------+-----------+---------------+--------------+
		// | Revision N  (≥0)   | ✓ (M=N)    | ✗ -1      | ✓ / ✗ -10 [2] | ✗ -100       |
		// | NO_STREAM   (-1)   | ✗ M        | ✓         | ✓             | ✓/✗ -100 [1] |
		// | ANY         (-2)   | ✓          | ✓         | ✓             | ✓/✗ -100 [1] |
		// | EXISTS      (-4)   | ✓          | ✗ -1      | ✗ -10         | ✗ -100       |
		// | DELETED     (-10)  | ✗ M        | ✗ -1      | ✓             | ✗ -100       |
		// | TOMBSTONED  (-100) | ✓          | ✗ -1      | ✗ -10         | ✓            |
		// +--------------------+------------+-----------+---------------+--------------+
		//
		// ✓ = success (no violation), ✗ = violation (actual_state value shown)

		// [1] Contextual: ✗ for append target (can't write to tombstoned stream),
		//     but should be ✓ for consistency checks (tombstoned = does not exist).
		// [2] Open question: should the actual_state return M or -10?
		//
		// Example message:
		//   Failed to append transaction due to consistency violations.
		//
		//    - stream-1: Stream already exists at revision 5.
		//    - stream-2: Stream is deleted but was expected to be at revision 10.
		// ----------------------------------------------------------------------------------

		Debug.Assert(violations.Count > 0, "The violations list must not be empty!");

		var message = FormatMessage(violations);

		var details = new AppendConsistencyViolationErrorDetails {
			Violations = { violations }
		};

		return RpcExceptions.FromError(StreamsError.AppendConsistencyViolation, message, details);

		static string FormatMessage(List<ConsistencyViolation> violations) {
			var builder = new StringBuilder("Append failed due to consistency violations.");

			var streamStates = violations
				.Where(v => v.TypeCase is ConsistencyViolation.TypeOneofCase.StreamState)
				.Select(v => v.StreamState)
				.ToList();

			if (streamStates.Count > 0) {
				builder.AppendLine();
				foreach (var ssv in streamStates)
					builder.AppendLine().Append($" - {FormatViolation(ssv)}");
			}

			return builder.ToString();
		}

		static string FormatViolation(ConsistencyViolation.Types.StreamStateViolation v) =>
			(v.ExpectedState, v.ActualState) switch {
				// REVISION
				(>= 0, >= 0) => $"Stream '{v.Stream}' is at revision {v.ActualState}, expected revision {v.ExpectedState}.",
				(>= 0, ActualStreamCondition.NotFound) => $"Stream '{v.Stream}' does not exist, expected revision {v.ExpectedState}.",
				(>= 0, ActualStreamCondition.Deleted) => $"Stream '{v.Stream}' is deleted, expected revision {v.ExpectedState}.",
				(>= 0, ActualStreamCondition.Tombstoned) => $"Stream '{v.Stream}' is tombstoned, expected revision {v.ExpectedState}.",

				// NO_STREAM
				(ExpectedStreamCondition.NoStream, >= 0) => $"Stream '{v.Stream}' already exists at revision {v.ActualState}.",
				(ExpectedStreamCondition.NoStream, ActualStreamCondition.Tombstoned) => $"Stream '{v.Stream}' is tombstoned.", // only on target stream

				// ANY
				(ExpectedStreamCondition.Any, ActualStreamCondition.Tombstoned) => $"Stream '{v.Stream}' is tombstoned.",
				(ExpectedStreamCondition.Any, ActualStreamCondition.NotFound) => $"Stream '{v.Stream}' is tombstoned.", // only on target stream when no checks were provided

				// EXISTS
				(ExpectedStreamCondition.Exists, ActualStreamCondition.NotFound) => $"Stream '{v.Stream}' does not exist.",
				(ExpectedStreamCondition.Exists, ActualStreamCondition.Deleted) => $"Stream '{v.Stream}' is deleted.",
				(ExpectedStreamCondition.Exists, ActualStreamCondition.Tombstoned) => $"Stream '{v.Stream}' is tombstoned.",

				// DELETED
				(ExpectedStreamCondition.Deleted, >= 0) => $"Stream '{v.Stream}' is at revision {v.ActualState}, explicitly expected deleted.",
				(ExpectedStreamCondition.Deleted, ActualStreamCondition.NotFound) => $"Stream '{v.Stream}' does not exist, explicitly expected deleted.",
				(ExpectedStreamCondition.Deleted, ActualStreamCondition.Tombstoned) => $"Stream '{v.Stream}' is tombstoned, explicitly expected deleted.",

				// TOMBSTONED
				(ExpectedStreamCondition.Tombstoned, ActualStreamCondition.NotFound) => $"Stream '{v.Stream}' does not exist, explicitly expected tombstoned.",
				(ExpectedStreamCondition.Tombstoned, ActualStreamCondition.Deleted) => $"Stream '{v.Stream}' is deleted, explicitly expected tombstoned.",

				// WHATEVER
				_ => $"Stream '{v.Stream}': expected state {v.ExpectedState}, actual state {v.ActualState}."
			};
	}
}
