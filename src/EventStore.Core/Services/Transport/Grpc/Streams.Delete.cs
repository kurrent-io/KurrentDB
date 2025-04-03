// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Core.Services.Transport.Common;
using Grpc.Core;
using static EventStore.Client.Streams.TombstoneReq.Types.Options;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Streams<TStreamId> {
	public override async Task<DeleteResp> Delete(DeleteReq request, ServerCallContext context) {
		using var duration = _deleteTracker.Start();
		try {
			var options = request.Options;
			var streamName = options.StreamIdentifier;
			var expectedVersion = options.ExpectedStreamRevisionCase switch {
				DeleteReq.Types.Options.ExpectedStreamRevisionOneofCase.Revision =>
				new StreamRevision(options.Revision).ToInt64(),
				DeleteReq.Types.Options.ExpectedStreamRevisionOneofCase.Any =>
				AnyStreamRevision.Any.ToInt64(),
				DeleteReq.Types.Options.ExpectedStreamRevisionOneofCase.NoStream =>
				AnyStreamRevision.NoStream.ToInt64(),
				DeleteReq.Types.Options.ExpectedStreamRevisionOneofCase.StreamExists =>
				AnyStreamRevision.StreamExists.ToInt64(),
				_ => throw RpcExceptions.InvalidArgument(options.ExpectedStreamRevisionCase)
			};

			var user = context.GetHttpContext().User;
			var op = DeleteOperation.WithParameter(Plugins.Authorization.Operations.Streams.Parameters.StreamId(streamName));
			if (!await _provider.CheckAccessAsync(user, op, context.CancellationToken)) {
				throw RpcExceptions.AccessDenied();
			}
			var requiresLeader = GetRequiresLeader(context.RequestHeaders);

			var position = await DeleteInternal(streamName, expectedVersion, user, false, requiresLeader,
				context.CancellationToken);

			return position.HasValue
				? new DeleteResp {
					Position = new DeleteResp.Types.Position {
						CommitPosition = position.Value.CommitPosition,
						PreparePosition = position.Value.PreparePosition
					}
				}
				: new DeleteResp {
					NoPosition = new Empty()
				};
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}

	public override async Task<TombstoneResp> Tombstone(TombstoneReq request, ServerCallContext context) {
		using var duration = _tombstoneTracker.Start();
		try {
			var options = request.Options;
			var streamName = options.StreamIdentifier;

			var expectedVersion = options.ExpectedStreamRevisionCase switch {
				ExpectedStreamRevisionOneofCase.Revision => new StreamRevision(options.Revision).ToInt64(),
				ExpectedStreamRevisionOneofCase.Any => AnyStreamRevision.Any.ToInt64(),
				ExpectedStreamRevisionOneofCase.NoStream => AnyStreamRevision.NoStream.ToInt64(),
				ExpectedStreamRevisionOneofCase.StreamExists => AnyStreamRevision.StreamExists.ToInt64(),
				_ => throw RpcExceptions.InvalidArgument(options.ExpectedStreamRevisionCase)
			};

			var requiresLeader = GetRequiresLeader(context.RequestHeaders);

			var user = context.GetHttpContext().User;
			var op = DeleteOperation.WithParameter(Plugins.Authorization.Operations.Streams.Parameters.StreamId(streamName));
			if (!await _provider.CheckAccessAsync(user, op, context.CancellationToken)) {
				throw RpcExceptions.AccessDenied();
			}

			var position = await DeleteInternal(streamName, expectedVersion, user, true, requiresLeader,
				context.CancellationToken);

			return position.HasValue
				? new TombstoneResp {
					Position = new TombstoneResp.Types.Position {
						CommitPosition = position.Value.CommitPosition,
						PreparePosition = position.Value.PreparePosition
					}
				}
				: new TombstoneResp {
					NoPosition = new Empty()
				};
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}

	private async Task<Position?> DeleteInternal(string streamName, long expectedVersion,
		ClaimsPrincipal user, bool hardDelete, bool requiresLeader, CancellationToken cancellationToken) {
		var correlationId = Guid.NewGuid(); // TODO: JPB use request id?
		var deleteResponseSource = new TaskCompletionSource<Position?>();

		var envelope = new CallbackEnvelope(HandleStreamDeletedCompleted);

		_publisher.Publish(new ClientMessage.DeleteStream(
			correlationId,
			correlationId,
			envelope,
			requiresLeader,
			streamName,
			expectedVersion,
			hardDelete,
			user,
			cancellationToken: cancellationToken));

		return await deleteResponseSource.Task;

		void HandleStreamDeletedCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled &&
			    RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
				deleteResponseSource.TrySetException(ex);
				return;
			}

			if (message is not ClientMessage.DeleteStreamCompleted completed) {
				deleteResponseSource.TrySetException(
					RpcExceptions.UnknownMessage<ClientMessage.DeleteStreamCompleted>(message));
				return;
			}

			switch (completed.Result) {
				case OperationResult.Success:
					deleteResponseSource.TrySetResult(completed.CommitPosition == -1
						? default
						: Position.FromInt64(completed.CommitPosition, completed.PreparePosition));

					return;
				case OperationResult.PrepareTimeout:
				case OperationResult.CommitTimeout:
				case OperationResult.ForwardTimeout:
					deleteResponseSource.TrySetException(RpcExceptions.Timeout(completed.Message));
					return;
				case OperationResult.WrongExpectedVersion:
					deleteResponseSource.TrySetException(RpcExceptions.WrongExpectedVersion("Delete",
						streamName, expectedVersion, completed.CurrentVersion));
					return;
				case OperationResult.StreamDeleted:
					deleteResponseSource.TrySetException(RpcExceptions.StreamDeleted(streamName));
					return;
				case OperationResult.InvalidTransaction:
					deleteResponseSource.TrySetException(RpcExceptions.InvalidTransaction());
					return;
				case OperationResult.AccessDenied:
					deleteResponseSource.TrySetException(RpcExceptions.AccessDenied());
					return;
				default:
					deleteResponseSource.TrySetException(RpcExceptions.UnknownError(completed.Result));
					return;
			}
		}
	}
}
