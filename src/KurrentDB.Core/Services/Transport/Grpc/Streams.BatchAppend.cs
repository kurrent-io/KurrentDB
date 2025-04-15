// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using EventStore.Plugins.Authorization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Grpc;
using Serilog;
using static EventStore.Client.Streams.BatchAppendReq.Types;
using static EventStore.Client.Streams.BatchAppendReq.Types.Options;
using static KurrentDB.Core.Messages.OperationResult;
using Empty = Google.Protobuf.WellKnownTypes.Empty;
using OperationResult = KurrentDB.Core.Messages.OperationResult;
using Status = Google.Rpc.Status;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

partial class Streams<TStreamId> {
	public override async Task BatchAppend(IAsyncStreamReader<BatchAppendReq> requestStream,
		IServerStreamWriter<BatchAppendResp> responseStream, ServerCallContext context) {
		var worker = new BatchAppendWorker(_publisher, _provider,
			_batchAppendTracker,
			requestStream, responseStream,
			context.GetHttpContext().User, _maxAppendSize, _maxAppendEventSize, _writeTimeout,
			GetRequiresLeader(context.RequestHeaders));

		await worker.Work(context.CancellationToken);
	}

	private class BatchAppendWorker {
		private readonly IPublisher _publisher;
		private readonly IAuthorizationProvider _authorizationProvider;
		private readonly IDurationTracker _tracker;
		private readonly IAsyncStreamReader<BatchAppendReq> _requestStream;
		private readonly IServerStreamWriter<BatchAppendResp> _responseStream;
		private readonly ClaimsPrincipal _user;
		private readonly int _maxAppendSize;
		private readonly int _maxAppendEventSize;
		private readonly TimeSpan _writeTimeout;
		private readonly bool _requiresLeader;
		private readonly Channel<BatchAppendResp> _channel;

		private long _pending;

		public BatchAppendWorker(IPublisher publisher, IAuthorizationProvider authorizationProvider,
			IDurationTracker tracker,
			IAsyncStreamReader<BatchAppendReq> requestStream, IServerStreamWriter<BatchAppendResp> responseStream,
			ClaimsPrincipal user, int maxAppendSize, int maxAppendEventSize, TimeSpan writeTimeout, bool requiresLeader) {
			_publisher = publisher;
			_authorizationProvider = authorizationProvider;
			_tracker = tracker;
			_requestStream = requestStream;
			_responseStream = responseStream;
			_user = user;
			_maxAppendSize = maxAppendSize;
			_maxAppendEventSize = maxAppendEventSize;
			_writeTimeout = writeTimeout;
			_requiresLeader = requiresLeader;
			_channel = Channel.CreateUnbounded<BatchAppendResp>(new() {
				AllowSynchronousContinuations = false,
				SingleReader = false,
				SingleWriter = false
			});
		}

		public Task Work(CancellationToken cancellationToken) {
			var remaining = 2;
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

#if DEBUG
			var sendTask =
#endif
					Send(_channel.Reader, cancellationToken)
						.ContinueWith(HandleCompletion, CancellationToken.None);
#if DEBUG
			var receiveTask =
#endif
					Receive(_channel.Writer, _user, _requiresLeader, cancellationToken)
						.ContinueWith(HandleCompletion, CancellationToken.None);

			return tcs.Task;

			async void HandleCompletion(Task task) {
				try {
					await task;
					if (Interlocked.Decrement(ref remaining) == 0) {
						tcs.TrySetResult();
					}
				} catch (OperationCanceledException) {
					tcs.TrySetCanceled(cancellationToken);
				} catch (IOException ex) {
					Log.Information("Closing gRPC client connection: {message}", ex.GetBaseException().Message);
					tcs.TrySetException(ex);
				} catch (Exception ex) {
					tcs.TrySetException(ex);
				}
			}
		}

		private async Task Send(ChannelReader<BatchAppendResp> reader, CancellationToken cancellationToken) {
			var isClosing = false;
			await foreach (var response in reader.ReadAllAsync(cancellationToken)) {
				if (!response.IsClosing) {
					await _responseStream.WriteAsync(response);
					if (Interlocked.Decrement(ref _pending) >= 0 && isClosing) {
						break;
					}
				} else {
					isClosing = true;
				}
			}
		}

		private async Task Receive(ChannelWriter<BatchAppendResp> writer, ClaimsPrincipal user, bool requiresLeader,
			CancellationToken cancellationToken) {
			var pendingWrites = new ConcurrentDictionary<Guid, ClientWriteRequest>();

			try {
				await foreach (var request in _requestStream.ReadAllAsync(cancellationToken)) {
					using var duration = _tracker.Start();
					try {
						var correlationId = Uuid.FromDto(request.CorrelationId).ToGuid();

						if (request.Options != null) {
							var timeout = Min(GetRequestedTimeout(request.Options), _writeTimeout);

							if (!await _authorizationProvider.CheckAccessAsync(user, WriteOperation.WithParameter(
								Plugins.Authorization.Operations.Streams.Parameters.StreamId(
									request.Options.StreamIdentifier)), cancellationToken)) {
								await writer.WriteAsync(new BatchAppendResp {
									CorrelationId = request.CorrelationId,
									StreamIdentifier = request.Options.StreamIdentifier,
									Error = Status.AccessDenied
								}, cancellationToken);
								continue;
							}

							if (request.Options.StreamIdentifier == null) {
								await writer.WriteAsync(new BatchAppendResp {
									CorrelationId = request.CorrelationId,
									StreamIdentifier = request.Options.StreamIdentifier,
									Error = Status.BadRequest(
										$"Required field {nameof(request.Options.StreamIdentifier)} not set.")
								}, cancellationToken);
								continue;
							}

							if (Max(timeout, TimeSpan.Zero) == TimeSpan.Zero) {
								await writer.WriteAsync(new BatchAppendResp {
									CorrelationId = request.CorrelationId,
									StreamIdentifier = request.Options.StreamIdentifier,
									Error = Status.Timeout
								}, cancellationToken);
								continue;
							}

							pendingWrites.AddOrUpdate(correlationId,
								c => FromOptions(c, request.Options, timeout, cancellationToken),
								(_, writeRequest) => writeRequest);
						}

						if (!pendingWrites.TryGetValue(correlationId, out var clientWriteRequest)) {
							continue;
						}

						try {
							clientWriteRequest.AddEvents(request.ProposedMessages.Select(FromProposedMessage), _maxAppendEventSize);

							if (clientWriteRequest.Size > _maxAppendSize) {
								pendingWrites.TryRemove(correlationId, out _);
								await writer.WriteAsync(new BatchAppendResp {
									CorrelationId = request.CorrelationId,
									StreamIdentifier = clientWriteRequest.StreamId,
									Error = Status.MaximumAppendSizeExceeded((uint)_maxAppendSize)
								}, cancellationToken);
							}
						} catch (MaxAppendEventSizeExceededException ex) {
							pendingWrites.TryRemove(correlationId, out _);
							await writer.WriteAsync(new BatchAppendResp {
								CorrelationId = request.CorrelationId,
								StreamIdentifier = clientWriteRequest.StreamId,
								Error = Status.MaximumAppendEventSizeExceeded(ex.EventId, (uint)ex.ProposedEventSize, (uint)ex.MaxAppendEventSize)
							}, cancellationToken);
						}

						if (!request.IsFinal) {
							continue;
						}

						if (!pendingWrites.TryRemove(correlationId, out _)) {
							continue;
						}

						Interlocked.Increment(ref _pending);

						_publisher.Publish(ToInternalMessage(clientWriteRequest, new CallbackEnvelope(message => {
							try {
								writer.TryWrite(ConvertMessage(message));
							} catch (Exception ex) {
								writer.TryComplete(ex);
							}
						}), requiresLeader, user, cancellationToken));

						BatchAppendResp ConvertMessage(Message message) {
							var batchAppendResp = message switch {
								ClientMessage.NotHandled notHandled => new BatchAppendResp {
									Error = new Status {
										Details = Any.Pack(new Empty()),
										Message = (notHandled.Reason, AdditionalInfo: notHandled.LeaderInfo) switch {
											(ClientMessage.NotHandled.Types.NotHandledReason.NotReady, _) => "Server Is Not Ready",
											(ClientMessage.NotHandled.Types.NotHandledReason.TooBusy, _) => "Server Is Busy",
											(ClientMessage.NotHandled.Types.NotHandledReason.NotLeader or ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly,
												ClientMessage.NotHandled.Types.LeaderInfo leaderInfo) =>
												throw RpcExceptions.LeaderInfo(leaderInfo.Http.GetHost(),
													leaderInfo.Http.GetPort()),
											(ClientMessage.NotHandled.Types.NotHandledReason.NotLeader or ClientMessage.NotHandled.Types.NotHandledReason.IsReadOnly, _) =>
												"No leader info available in response",
											_ => $"Unknown {nameof(ClientMessage.NotHandled.Types.NotHandledReason)} ({(int)notHandled.Reason})"
										}
									}
								},
								ClientMessage.WriteEventsCompleted completed => completed.Result switch {
									Success => new BatchAppendResp {
										Success = BatchAppendResp.Types.Success.Completed(completed.CommitPosition,
											completed.PreparePosition, completed.LastEventNumber),
									},
									OperationResult.WrongExpectedVersion => new BatchAppendResp {
										Error = Status.WrongExpectedVersion(
											StreamRevision.FromInt64(completed.CurrentVersion),
											clientWriteRequest.ExpectedVersion)
									},
									OperationResult.AccessDenied => new BatchAppendResp { Error = Status.AccessDenied },
									OperationResult.StreamDeleted => new BatchAppendResp {
										Error = Status.StreamDeleted(clientWriteRequest.StreamId)
									},
									CommitTimeout or ForwardTimeout or PrepareTimeout => new BatchAppendResp { Error = Status.Timeout },
									_ => new BatchAppendResp { Error = Status.Unknown }
								},
								_ => new BatchAppendResp {
									Error = Status.InternalError(
										$"Envelope callback expected either {nameof(ClientMessage.WriteEventsCompleted)} or {nameof(ClientMessage.NotHandled)}, received {message.GetType().Name} instead.")
								}
							};
							batchAppendResp.CorrelationId = request.CorrelationId;
							batchAppendResp.StreamIdentifier = new StreamIdentifier {
								StreamName = ByteString.CopyFromUtf8(clientWriteRequest.StreamId)
							};
							return batchAppendResp;
						}
					} catch (Exception ex) {
						duration.SetException(ex);
						await writer.WriteAsync(new BatchAppendResp {
							CorrelationId = request.CorrelationId,
							StreamIdentifier = request.Options.StreamIdentifier,
							Error = Status.BadRequest(ex.Message)
						}, cancellationToken);
					}
				}

				await writer.WriteAsync(new BatchAppendResp {
					IsClosing = true
				}, cancellationToken);
			} catch (Exception ex) {
				writer.TryComplete(ex);
				throw;
			}

			ClientWriteRequest FromOptions(Guid correlationId, Options options, TimeSpan timeout,
				CancellationToken cancellationToken) =>
				new(correlationId, options.StreamIdentifier, options.ExpectedStreamPositionCase switch {
					ExpectedStreamPositionOneofCase.StreamPosition => new StreamRevision(options.StreamPosition).ToInt64(),
					ExpectedStreamPositionOneofCase.Any => AnyStreamRevision.Any.ToInt64(),
					ExpectedStreamPositionOneofCase.StreamExists => AnyStreamRevision.StreamExists.ToInt64(),
					ExpectedStreamPositionOneofCase.NoStream => AnyStreamRevision.NoStream.ToInt64(),
					_ => throw RpcExceptions.InvalidArgument(options.ExpectedStreamPositionCase)
				}, timeout, () =>
					pendingWrites.TryRemove(correlationId, out var pendingWrite)
						? writer.WriteAsync(new BatchAppendResp {
							CorrelationId = Uuid.FromGuid(correlationId).ToDto(),
							StreamIdentifier = new StreamIdentifier {
								StreamName = ByteString.CopyFromUtf8(pendingWrite.StreamId)
							},
							Error = Status.Timeout
						}, cancellationToken)
						: new ValueTask(Task.CompletedTask), cancellationToken);

			static Event FromProposedMessage(ProposedMessage proposedMessage) {
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

				return new(Uuid.FromDto(proposedMessage.Id).ToGuid(),
					eventType,
					contentType ==
					Constants.Metadata.ContentTypes.ApplicationJson, proposedMessage.Data.ToByteArray(),
					proposedMessage.CustomMetadata.ToByteArray(), dataSchemaInfo, metadataSchemaInfo);
			}

			static ClientMessage.WriteEvents ToInternalMessage(ClientWriteRequest request, IEnvelope envelope,
				bool requiresLeader, ClaimsPrincipal user, CancellationToken token) =>
				new(Guid.NewGuid(), request.CorrelationId, envelope, requiresLeader, request.StreamId,
					request.ExpectedVersion, request.Events.ToArray(), user, cancellationToken: token);

			static TimeSpan GetRequestedTimeout(Options options) => options.DeadlineOptionCase switch {
				DeadlineOptionOneofCase.Deadline => options.Deadline.ToTimeSpan(),
				_ => (options.Deadline21100?.ToDateTime() ?? DateTime.MaxValue) - DateTime.UtcNow,
			};

			static TimeSpan Min(TimeSpan a, TimeSpan b) => a > b ? b : a;
			static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
		}
	}

	private class MaxAppendEventSizeExceededException(string eventId, int proposedEventSize, int maxAppendEventSize)
		: Exception($"Event with Id: {eventId}, Size: {proposedEventSize}, exceeded Max Append Event Size of {maxAppendEventSize}") {
		public string EventId { get; } = eventId;
		public int ProposedEventSize { get; } = proposedEventSize;
		public int MaxAppendEventSize { get; } = maxAppendEventSize;
	}

	private record ClientWriteRequest {
		public Guid CorrelationId { get; }
		public string StreamId { get; }
		public long ExpectedVersion { get; }
		private readonly List<Event> _events;
		public IEnumerable<Event> Events => _events.AsEnumerable();
		private int _size;
		public int Size => _size;

		public ClientWriteRequest(Guid correlationId, string streamId, long expectedVersion, TimeSpan timeout,
			Func<ValueTask> onTimeout, CancellationToken cancellationToken) {
			CorrelationId = correlationId;
			StreamId = streamId;
			_events = [];
			_size = 0;
			ExpectedVersion = expectedVersion;

			Task.Delay(timeout, cancellationToken).ContinueWith(_ => onTimeout(), cancellationToken);
		}

		public ClientWriteRequest AddEvents(IEnumerable<Event> events, int maxAppendEventSize) {
			foreach (var e in events) {
				var eventSize = Event.SizeOnDisk(e.EventType, e.Data, e.Metadata);
				if (eventSize > maxAppendEventSize) {
					throw new MaxAppendEventSizeExceededException(e.EventId.ToString(), eventSize, maxAppendEventSize);
				}

				_size += eventSize;
				_events.Add(e);
			}

			return this;
		}
	}
}
