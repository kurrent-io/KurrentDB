// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Protocol.V2;
using Serilog;

namespace KurrentDB.Core.Services.Transport.Grpc;

// Logic for converting ClientMessage responses to gRPC MultiStreamAppend responses
public class MSAResponseConverter {
	private static readonly ILogger Log = Serilog.Log.ForContext(typeof(MSAResponseConverter));
	private static readonly ErrorDetails.Types.StreamDeleted StreamDeletedError = new();

	private readonly int _chunkSize;

	public MSAResponseConverter(int chunkSize) {
		_chunkSize = chunkSize;
	}

	public MultiStreamAppendResponse ConvertToMSAResponse(
		IReadOnlyList<AppendStreamRequest> appendStreamRequests,
		Message message) {

		if (message is ClientMessage.NotHandled notHandled &&
			RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
			throw ex;
		}

		if (message is not ClientMessage.WriteEventsCompleted completed) {
			throw RpcExceptions.UnknownMessage<ClientMessage.WriteEventsCompleted>(message);
		}

		return ConvertToMSAResponse(appendStreamRequests, completed);
	}

	// This needs the appendStreamRequests so that it can understand which streams
	// the response is referring to
	private MultiStreamAppendResponse ConvertToMSAResponse(
		IReadOnlyList<AppendStreamRequest> appendStreamRequests,
		ClientMessage.WriteEventsCompleted completed) {

		switch (completed.Result) {
			case OperationResult.Success: {
				// successful MultiStreamAppendResponse response has one AppendStreamSuccess per AppendStreamRequest.
				Debug.Assert(completed.LastEventNumbers.Length == appendStreamRequests.Count);

				if (completed.CommitPosition < 0) {
					// this should never happen, but sending -1 to the client would be bad.
					var message = $"Unexpected error: Commit position was unexpectedly less than 0: {completed.CommitPosition}";
					Log.Error(message);
					throw new InvalidOperationException(message);
				}

				var multiResponse = new MultiStreamAppendResponse { Success = new() };

				for (var i = 0; i < completed.LastEventNumbers.Length; i++) {
					multiResponse.Success.Output.Add(new AppendStreamSuccess {
						Stream = appendStreamRequests[i].Stream,
						StreamRevision = completed.LastEventNumbers.Span[i],
						// Position for the transaction - which is the same for every stream
						Position = completed.CommitPosition,
					});
				}

				return multiResponse;
			}

			case OperationResult.WrongExpectedVersion: {
				// wrong expected version MultiStreamAppendResponse one AppendStreamFailure per failure.
				Debug.Assert(completed.FailureCurrentVersions.Length == completed.FailureStreamIndexes.Length);

				var multiResponse = new MultiStreamAppendResponse { Failure = new() };

				for (var i = 0; i < completed.FailureCurrentVersions.Length; i++) {
					multiResponse.Failure.Output.Add(new AppendStreamFailure {
						Stream = appendStreamRequests[completed.FailureStreamIndexes.Span[i]].Stream,
						WrongExpectedRevision = new() {
							StreamRevision = completed.FailureCurrentVersions.Span[i],
						},
					});
				}

				return multiResponse;
			}

			case OperationResult.StreamDeleted: {
				return new MultiStreamAppendResponse {
					Failure = new() {
						Output = {
							new AppendStreamFailure {
								Stream = appendStreamRequests[completed.FailureStreamIndexes.Span[0]].Stream,
								StreamDeleted = StreamDeletedError,
							},
						},
					},
				};
			}

			// this is not a possible case on the gRPC paths, but we handle it anyway
			case OperationResult.AccessDenied: {
				return new MultiStreamAppendResponse {
					Failure = new() {
						Output = {
							new AppendStreamFailure {
								Stream = "<unknown>",
								AccessDenied = new(),
							},
						},
					},
				};
			}

			case OperationResult.InvalidTransaction:
				return new MultiStreamAppendResponse {
					Failure = new() {
						Output = {
							new AppendStreamFailure {
								TransactionMaxSizeExceeded = new() {
									MaxSize = _chunkSize,
								},
							},
						},
					},
				};

			case OperationResult.PrepareTimeout:
			case OperationResult.CommitTimeout:
			case OperationResult.ForwardTimeout:
				throw RpcExceptions.Timeout(completed.Message);

			default:
				throw RpcExceptions.UnknownError(completed.Result);
		}
	}
}
