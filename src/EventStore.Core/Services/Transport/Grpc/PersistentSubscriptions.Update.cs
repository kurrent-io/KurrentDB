using System;
using System.Threading.Tasks;
using EventStore.Client.PersistentSubscriptions;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using AllOptionOneofCase = EventStore.Client.PersistentSubscriptions.UpdateReq.Types.AllOptions.AllOptionOneofCase;
using RevisionOptionOneofCase = EventStore.Client.PersistentSubscriptions.UpdateReq.Types.StreamOptions.RevisionOptionOneofCase;
using StreamOptionOneofCase = EventStore.Client.PersistentSubscriptions.UpdateReq.Types.Options.StreamOptionOneofCase;

namespace EventStore.Core.Services.Transport.Grpc {
	internal partial class PersistentSubscriptions {
		private static readonly Operation UpdateOperation = new Operation(Plugins.Authorization.Operations.Subscriptions.Update);
		public override async Task<UpdateResp> Update(UpdateReq request, ServerCallContext context) {
			var updatePersistentSubscriptionSource = new TaskCompletionSource<UpdateResp>();
			var settings = request.Options.Settings;
			var correlationId = Guid.NewGuid();

			var user = context.GetHttpContext().User;
			if (!await _authorizationProvider.CheckAccessAsync(user,
				UpdateOperation, context.CancellationToken).ConfigureAwait(false)) {
				throw RpcExceptions.AccessDenied();
			}

			string streamId = null;

			switch (request.Options.StreamOptionCase) {
				case StreamOptionOneofCase.Stream:
				case StreamOptionOneofCase.None: /*for backwards compatibility*/
				{
					StreamRevision startRevision;

					if (request.Options.StreamOptionCase == StreamOptionOneofCase.Stream) {
						streamId = request.Options.Stream.StreamIdentifier;
						startRevision = request.Options.Stream.RevisionOptionCase switch {
							RevisionOptionOneofCase.Revision => new StreamRevision(request.Options.Stream.Revision),
							RevisionOptionOneofCase.Start => StreamRevision.Start,
							RevisionOptionOneofCase.End => StreamRevision.End,
							_ => throw RpcExceptions.InvalidArgument(request.Options.Stream.RevisionOptionCase)
						};
					} else { /*for backwards compatibility*/
#pragma warning disable 612
						streamId = request.Options.StreamIdentifier;
						startRevision = new StreamRevision(request.Options.Settings.Revision);
#pragma warning restore 612
					}

					_publisher.Publish(new ClientMessage.UpdatePersistentSubscriptionToStream(
						correlationId,
						correlationId,
						new CallbackEnvelope(HandleUpdatePersistentSubscriptionCompleted),
						streamId,
						request.Options.GroupName,
						settings.ResolveLinks,
						startRevision.ToInt64(),
						settings.MessageTimeoutCase switch {
							UpdateReq.Types.Settings.MessageTimeoutOneofCase.MessageTimeoutMs => settings.MessageTimeoutMs,
							UpdateReq.Types.Settings.MessageTimeoutOneofCase.MessageTimeoutTicks => (int)TimeSpan
								.FromTicks(settings.MessageTimeoutTicks).TotalMilliseconds,
							_ => 0
						},
						settings.ExtraStatistics,
						settings.MaxRetryCount,
						settings.HistoryBufferSize,
						settings.LiveBufferSize,
						settings.ReadBatchSize,
						settings.CheckpointAfterCase switch {
							UpdateReq.Types.Settings.CheckpointAfterOneofCase.CheckpointAfterMs => settings.CheckpointAfterMs,
							UpdateReq.Types.Settings.CheckpointAfterOneofCase.CheckpointAfterTicks => (int)TimeSpan
								.FromTicks(settings.CheckpointAfterTicks).TotalMilliseconds,
							_ => 0
						},
						settings.MinCheckpointCount,
						settings.MaxCheckpointCount,
						settings.MaxSubscriberCount,
						settings.NamedConsumerStrategy.ToString(),
						user));
					break;
				}
				case StreamOptionOneofCase.All:
					var startPosition = request.Options.All.AllOptionCase switch {
						AllOptionOneofCase.Position => new Position(
							request.Options.All.Position.CommitPosition,
							request.Options.All.Position.PreparePosition),
						AllOptionOneofCase.Start => Position.Start,
						AllOptionOneofCase.End => Position.End,
						_ => throw new InvalidOperationException()
					};

					streamId = SystemStreams.AllStream;

					_publisher.Publish(new ClientMessage.UpdatePersistentSubscriptionToAll(
						correlationId,
						correlationId,
						new CallbackEnvelope(HandleUpdatePersistentSubscriptionCompleted),
						request.Options.GroupName,
						settings.ResolveLinks,
						new TFPos(
							startPosition.ToInt64().commitPosition,
							startPosition.ToInt64().preparePosition
						),
						settings.MessageTimeoutCase switch {
							UpdateReq.Types.Settings.MessageTimeoutOneofCase.MessageTimeoutMs => settings.MessageTimeoutMs,
							UpdateReq.Types.Settings.MessageTimeoutOneofCase.MessageTimeoutTicks => (int)TimeSpan
								.FromTicks(settings.MessageTimeoutTicks).TotalMilliseconds,
							_ => 0
						},
						settings.ExtraStatistics,
						settings.MaxRetryCount,
						settings.HistoryBufferSize,
						settings.LiveBufferSize,
						settings.ReadBatchSize,
						settings.CheckpointAfterCase switch {
							UpdateReq.Types.Settings.CheckpointAfterOneofCase.CheckpointAfterMs => settings.CheckpointAfterMs,
							UpdateReq.Types.Settings.CheckpointAfterOneofCase.CheckpointAfterTicks => (int)TimeSpan
								.FromTicks(settings.CheckpointAfterTicks).TotalMilliseconds,
							_ => 0
						},
						settings.MinCheckpointCount,
						settings.MaxCheckpointCount,
						settings.MaxSubscriberCount,
						settings.NamedConsumerStrategy.ToString(),
						user));
					break;
				default:
					throw new InvalidOperationException();
			}

			return await updatePersistentSubscriptionSource.Task.ConfigureAwait(false);

			void HandleUpdatePersistentSubscriptionCompleted(Message message) {
				if (message is ClientMessage.NotHandled notHandled && RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
					updatePersistentSubscriptionSource.TrySetException(ex);
					return;
				}

				if (streamId != SystemStreams.AllStream) {
					if (message is ClientMessage.UpdatePersistentSubscriptionToStreamCompleted completed) {
						switch (completed.Result) {
							case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted
								.UpdatePersistentSubscriptionToStreamResult.Success:
								updatePersistentSubscriptionSource.TrySetResult(new UpdateResp());
								return;
							case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted
								.UpdatePersistentSubscriptionToStreamResult.Fail:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.PersistentSubscriptionFailed(streamId, request.Options.GroupName,
										completed.Reason));
								return;
							case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted
								.UpdatePersistentSubscriptionToStreamResult
								.AccessDenied:
								updatePersistentSubscriptionSource.TrySetException(RpcExceptions.AccessDenied());
								return;
							case ClientMessage.UpdatePersistentSubscriptionToStreamCompleted
								.UpdatePersistentSubscriptionToStreamResult.DoesNotExist:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName));
								return;
							default:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.UnknownError(completed.Result));
								return;
						}
					}
					updatePersistentSubscriptionSource.TrySetException(
						RpcExceptions.UnknownMessage<ClientMessage.UpdatePersistentSubscriptionToStreamCompleted>(message));
				} else {
					if (message is ClientMessage.UpdatePersistentSubscriptionToAllCompleted completedAll) {
						switch (completedAll.Result) {
							case ClientMessage.UpdatePersistentSubscriptionToAllCompleted
								.UpdatePersistentSubscriptionToAllResult.Success:
								updatePersistentSubscriptionSource.TrySetResult(new UpdateResp());
								return;
							case ClientMessage.UpdatePersistentSubscriptionToAllCompleted
								.UpdatePersistentSubscriptionToAllResult.Fail:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.PersistentSubscriptionFailed(streamId, request.Options.GroupName,
										completedAll.Reason));
								return;
							case ClientMessage.UpdatePersistentSubscriptionToAllCompleted
								.UpdatePersistentSubscriptionToAllResult
								.AccessDenied:
								updatePersistentSubscriptionSource.TrySetException(RpcExceptions.AccessDenied());
								return;
							case ClientMessage.UpdatePersistentSubscriptionToAllCompleted
								.UpdatePersistentSubscriptionToAllResult.DoesNotExist:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.PersistentSubscriptionDoesNotExist(streamId, request.Options.GroupName));
								return;
							default:
								updatePersistentSubscriptionSource.TrySetException(
									RpcExceptions.UnknownError(completedAll.Result));
								return;
						}
					}
					updatePersistentSubscriptionSource.TrySetException(
						RpcExceptions.UnknownMessage<ClientMessage.UpdatePersistentSubscriptionToAllCompleted>(message));
				}
			}
		}
	}
}
