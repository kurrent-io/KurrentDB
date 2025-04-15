// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Client.Streams;
using Grpc.Core;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Grpc;
using static EventStore.Client.Streams.AppendReq.Types.Options;
using Empty = EventStore.Client.Empty;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Streams<TStreamId> {
	public override async Task<AppendResp> Append(IAsyncStreamReader<AppendReq> requestStream, ServerCallContext context) {
		using var duration = _appendTracker.Start();
		try {
			if (!await requestStream.MoveNext())
				throw new InvalidOperationException();

			if (requestStream.Current.ContentCase != AppendReq.ContentOneofCase.Options)
				throw new InvalidOperationException();

			var options = requestStream.Current.Options;
			var streamName = options.StreamIdentifier;

			var expectedVersion = options.ExpectedStreamRevisionCase switch {
				ExpectedStreamRevisionOneofCase.Revision => new StreamRevision(options.Revision).ToInt64(),
				ExpectedStreamRevisionOneofCase.Any => AnyStreamRevision.Any.ToInt64(),
				ExpectedStreamRevisionOneofCase.StreamExists => AnyStreamRevision.StreamExists.ToInt64(),
				ExpectedStreamRevisionOneofCase.NoStream => AnyStreamRevision.NoStream.ToInt64(),
				_ => throw RpcExceptions.InvalidArgument(options.ExpectedStreamRevisionCase)
			};

			var requiresLeader = GetRequiresLeader(context.RequestHeaders);

			var user = context.GetHttpContext().User;
			var op = WriteOperation.WithParameter(Plugins.Authorization.Operations.Streams.Parameters.StreamId(streamName));
			if (!await _provider.CheckAccessAsync(user, op, context.CancellationToken)) {
				throw RpcExceptions.AccessDenied();
			}

			var correlationId = Guid.NewGuid(); // TODO: JPB use request id?

			var events = new List<Event>();

			var size = 0;
			while (await requestStream.MoveNext()) {
				if (requestStream.Current.ContentCase != AppendReq.ContentOneofCase.ProposedMessage)
					throw new InvalidOperationException();

				var proposedMessage = requestStream.Current.ProposedMessage;
				var data = proposedMessage.Data.ToByteArray();
				var metadata = proposedMessage.CustomMetadata.ToByteArray();

                //TODO SS: schema registry integration
                // here we are using the new schema info
                // event type => schema name
                // content type => schema format
                // schema version id

				if (!proposedMessage.Metadata.TryGetValue(Constants.Metadata.Type, out var eventType)) {
					throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.Type);
				}

				if (!proposedMessage.Metadata.TryGetValue(Constants.Metadata.ContentType, out var contentType)) {
					throw RpcExceptions.RequiredMetadataPropertyMissing(Constants.Metadata.ContentType);
				}

				var dataSchemaInfo = SchemaInfo.None;
				if (proposedMessage.Metadata.TryGetValue(Constants.Metadata.SchemaVersionId, out var schemaVersion)) {
					dataSchemaInfo = new SchemaInfo(SchemaInfo.FormatFromString(contentType), new Guid(schemaVersion));
				}

				var metadataSchemaInfo = SchemaInfo.None;
				if (proposedMessage.Metadata.TryGetValue(Constants.Metadata.MetadataSchemaVersionId, out var metadataSchemaVersion)) {
					if (proposedMessage.Metadata.TryGetValue(Constants.Metadata.MetadataContentType, out var metadataContentType)) {
						metadataSchemaInfo = new SchemaInfo(SchemaInfo.FormatFromString(metadataContentType), new Guid(metadataSchemaVersion));
					}
				}

				var eventSize = Event.SizeOnDisk(eventType, data, metadata);
				if (eventSize > _maxAppendEventSize) {
					throw RpcExceptions.MaxAppendEventSizeExceeded(proposedMessage.Id.ToString(), eventSize, _maxAppendEventSize);
				}

				size += eventSize;

				if (size > _maxAppendSize) {
					throw RpcExceptions.MaxAppendSizeExceeded(_maxAppendSize);
				}

				events.Add(new Event(
					eventId: Uuid.FromDto(proposedMessage.Id).ToGuid(),
					eventType: eventType,
					isJson: contentType == Constants.Metadata.ContentTypes.ApplicationJson,
					data: data,
					metadata: metadata,
                    dataSchemaInfo: dataSchemaInfo,
                    metadataSchemaInfo: metadataSchemaInfo)
                );
			}

			var appendResponseSource = new TaskCompletionSource<AppendResp>(TaskCreationOptions.RunContinuationsAsynchronously);

			var envelope = new CallbackEnvelope(HandleWriteEventsCompleted);

			_publisher.Publish(new ClientMessage.WriteEvents(
				correlationId,
				correlationId,
				envelope,
				requiresLeader,
				streamName,
				expectedVersion,
				events.ToArray(),
				user,
				cancellationToken: context.CancellationToken));

			return await appendResponseSource.Task;

			void HandleWriteEventsCompleted(Message message) {
				if (message is ClientMessage.NotHandled notHandled &&
					RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
					appendResponseSource.TrySetException(ex);
					return;
				}

				if (message is not ClientMessage.WriteEventsCompleted completed) {
					appendResponseSource.TrySetException(
						RpcExceptions.UnknownMessage<ClientMessage.WriteEventsCompleted>(message));
					return;
				}

				var response = new AppendResp();
				switch (completed.Result) {
					case OperationResult.Success:
						response.Success = new AppendResp.Types.Success();
						if (completed.LastEventNumber == -1) {
							response.Success.NoStream = new Empty();
						} else {
							response.Success.CurrentRevision = StreamRevision.FromInt64(completed.LastEventNumber);
						}

						if (completed.CommitPosition == -1) {
							response.Success.NoPosition = new Empty();
						} else {
							var position = Position.FromInt64(completed.CommitPosition, completed.PreparePosition);
							response.Success.Position = new AppendResp.Types.Position {
								CommitPosition = position.CommitPosition,
								PreparePosition = position.PreparePosition
							};
						}

						appendResponseSource.TrySetResult(response);
						return;
					case OperationResult.PrepareTimeout:
					case OperationResult.CommitTimeout:
					case OperationResult.ForwardTimeout:
						appendResponseSource.TrySetException(RpcExceptions.Timeout(completed.Message));
						return;
					case OperationResult.WrongExpectedVersion:
						response.WrongExpectedVersion = new AppendResp.Types.WrongExpectedVersion();

						switch (options.ExpectedStreamRevisionCase) {
							case ExpectedStreamRevisionOneofCase.Any:
								response.WrongExpectedVersion.ExpectedAny = new Empty();
								response.WrongExpectedVersion.Any2060 = new Empty();
								break;
							case ExpectedStreamRevisionOneofCase.StreamExists:
								response.WrongExpectedVersion.ExpectedStreamExists = new Empty();
								response.WrongExpectedVersion.StreamExists2060 = new Empty();
								break;
							case ExpectedStreamRevisionOneofCase.NoStream:
								response.WrongExpectedVersion.ExpectedNoStream = new Empty();
								response.WrongExpectedVersion.ExpectedRevision2060 = ulong.MaxValue;
								break;
							case ExpectedStreamRevisionOneofCase.Revision:
								response.WrongExpectedVersion.ExpectedRevision =
									StreamRevision.FromInt64(expectedVersion);
								response.WrongExpectedVersion.ExpectedRevision2060 =
									StreamRevision.FromInt64(expectedVersion);
								break;
						}

						if (completed.CurrentVersion == -1) {
							response.WrongExpectedVersion.CurrentNoStream = new Empty();
							response.WrongExpectedVersion.NoStream2060 = new Empty();
						} else {
							response.WrongExpectedVersion.CurrentRevision =
								StreamRevision.FromInt64(completed.CurrentVersion);
							response.WrongExpectedVersion.CurrentRevision2060 =
								StreamRevision.FromInt64(completed.CurrentVersion);
						}

						appendResponseSource.TrySetResult(response);
						return;
					case OperationResult.StreamDeleted:
						appendResponseSource.TrySetException(RpcExceptions.StreamDeleted(streamName));
						return;
					case OperationResult.InvalidTransaction:
						appendResponseSource.TrySetException(RpcExceptions.InvalidTransaction());
						return;
					case OperationResult.AccessDenied:
						appendResponseSource.TrySetException(RpcExceptions.AccessDenied());
						return;
					default:
						appendResponseSource.TrySetException(RpcExceptions.UnknownError(completed.Result));
						return;
				}
			}
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}
}
