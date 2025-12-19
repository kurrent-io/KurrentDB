// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Streams;
using Google.Protobuf;
using Grpc.Core;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.Services.Transport.Grpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static EventStore.Client.Streams.ReadResp.Types;
using static EventStore.Plugins.Authorization.Operations.Streams;
using CountOptionOneofCase = EventStore.Client.Streams.ReadReq.Types.Options.CountOptionOneofCase;
using FilterOptionOneofCase = EventStore.Client.Streams.ReadReq.Types.Options.FilterOptionOneofCase;
using Position = KurrentDB.Core.Services.Transport.Common.Position;
using ReadDirection = EventStore.Client.Streams.ReadReq.Types.Options.Types.ReadDirection;
using StreamOptionOneofCase = EventStore.Client.Streams.ReadReq.Types.Options.StreamOptionOneofCase;

// ReSharper disable InvertIf

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Streams<TStreamId> {
	public override async Task Read(
		ReadReq request,
		IServerStreamWriter<ReadResp> responseStream,
		ServerCallContext context) {
		var trackDuration = request.Options.CountOptionCase != CountOptionOneofCase.Subscription;
		using var duration = trackDuration ? _readTracker.Start() : Duration.Nil;
		try {
			var options = request.Options;
			var countOptionsCase = options.CountOptionCase;
			var streamOptionsCase = options.StreamOptionCase;
			var readDirection = options.ReadDirection;
			var filterOptionsCase = options.FilterOptionCase;
			var compatibility = options.ControlOption?.Compatibility ?? 0;

			var httpContext = context.GetHttpContext();
			var user = httpContext.User;
			var requiresLeader = GetRequiresLeader(context.RequestHeaders);

			var uuidOption = options.UuidOption;
			if (uuidOption == null) {
				throw RpcExceptions.RequiredArgument<ReadReq.Types.Options.Types.UUIDOption>(nameof(uuidOption));
			}

			var op = streamOptionsCase switch {
				StreamOptionOneofCase.Stream => ReadOperation.WithParameter(Parameters.StreamId(request.Options.Stream.StreamIdentifier)),
				StreamOptionOneofCase.All => ReadOperation.WithParameter(Parameters.StreamId(SystemStreams.AllStream)),
				_ => throw RpcExceptions.InvalidArgument(streamOptionsCase)
			};

			if (!await _provider.CheckAccessAsync(user, op, context.CancellationToken)) {
				throw RpcExceptions.AccessDenied();
			}

			try {
				var enumerator = CreateEnumerator(
					request,
					user,
					requiresLeader,
					compatibility,
					streamOptionsCase,
					countOptionsCase,
					readDirection,
					filterOptionsCase,
					httpContext,
					context.CancellationToken);

				async void DisposeEnumerator() => await enumerator.DisposeAsync();

				await using (enumerator) {
					await using (context.CancellationToken.Register(DisposeEnumerator)) {
						while (await enumerator.MoveNextAsync()) {
							if (ResponseConverter.TryConvertReadResponse(enumerator.Current, uuidOption, out var readResponse))
								await responseStream.WriteAsync(readResponse);
						}
					}
				}
			} catch (ReadResponseException ex) {
				ResponseConverter.ConvertReadResponseException(ex);
			}
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}

	private IAsyncEnumerator<ReadResponse> CreateEnumerator(
		ReadReq request,
		ClaimsPrincipal user,
		bool requiresLeader,
		uint compatibility,
		StreamOptionOneofCase streamOptionsCase,
		CountOptionOneofCase countOptionsCase,
		ReadDirection readDirection,
		FilterOptionOneofCase filterOptionsCase,
		HttpContext httpContext,
		CancellationToken cancellationToken) {
		return (streamOptionsCase, countOptionsCase, readDirection, filterOptionsCase) switch {
			(StreamOptionOneofCase.Stream,
				CountOptionOneofCase.Count,
				ReadDirection.Forwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.ReadStreamForwards(
					_publisher,
					request.Options.Stream.StreamIdentifier,
					request.Options.Stream.ToStreamRevision(),
					request.Options.Count,
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					_expiryStrategy,
					compatibility,
					Enumerator.DefaultReadBatchSize,
					cancellationToken),
			(StreamOptionOneofCase.Stream,
				CountOptionOneofCase.Count,
				ReadDirection.Backwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.ReadStreamBackwards(
					_publisher,
					request.Options.Stream.StreamIdentifier,
					request.Options.Stream.ToStreamRevision(),
					request.Options.Count,
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					_expiryStrategy,
					compatibility,
					Enumerator.DefaultReadBatchSize,
					cancellationToken),
			(StreamOptionOneofCase.All,
				CountOptionOneofCase.Count,
				ReadDirection.Forwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.ReadAllForwards(
					_publisher,
					request.Options.All.ToPosition(),
					request.Options.Count,
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					_expiryStrategy,
					cancellationToken),
			(StreamOptionOneofCase.All,
				CountOptionOneofCase.Count,
				ReadDirection.Backwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.ReadAllBackwards(
					_publisher,
					request.Options.All.ToPosition(),
					request.Options.Count,
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					_expiryStrategy,
					cancellationToken),
			(StreamOptionOneofCase.All, CountOptionOneofCase.Count, ReadDirection.Forwards, FilterOptionOneofCase.Filter) => GetReadAllForwardsFilteredEnumerator(),
			(StreamOptionOneofCase.All, CountOptionOneofCase.Count, ReadDirection.Backwards, FilterOptionOneofCase.Filter) => GetReadAllBackwardsFilteredEnumerator(),
			(StreamOptionOneofCase.All, CountOptionOneofCase.Subscription, ReadDirection.Forwards, FilterOptionOneofCase.Filter) => GetAllSubscriptionFilteredEnumerator(),
			(StreamOptionOneofCase.Stream,
				CountOptionOneofCase.Subscription,
				ReadDirection.Forwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.StreamSubscription<TStreamId>(
					_publisher,
					_expiryStrategy,
					request.Options.Stream.StreamIdentifier,
					request.Options.Stream.ToSubscriptionStreamRevision(),
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					readBatchSize: Enumerator.DefaultReadBatchSize,
					catchUpBufferSize: Enumerator.DefaultCatchUpBufferSize,
					cancellationToken: cancellationToken),
			(StreamOptionOneofCase.All,
				CountOptionOneofCase.Subscription,
				ReadDirection.Forwards,
				FilterOptionOneofCase.NoFilter) => new Enumerator.AllSubscription(
					_publisher,
					_expiryStrategy,
					request.Options.All.ToSubscriptionPosition(),
					request.Options.ResolveLinks,
					user,
					requiresLeader,
					cancellationToken: cancellationToken),
			_ => throw RpcExceptions.InvalidCombination((streamOptionsCase, countOptionsCase, readDirection,
				filterOptionsCase))
		};

		IAsyncEnumerator<ReadResponse> GetFilterOrIndexEnumerator(
			Func<string, DuckDBConnectionPool, IAsyncEnumerator<ReadResponse>> getIndexEnumerator,
			Func<ReadReq.Types.Options.Types.FilterOptions, IAsyncEnumerator<ReadResponse>> getReadAllEnumerator
		) {
			var filter = request.Options.Filter;
			// Index reads require StreamIdentifier filter with one element that is the index name
			if (filter.FilterCase == ReadReq.Types.Options.Types.FilterOptions.FilterOneofCase.StreamIdentifier
				&& string.IsNullOrEmpty(filter.StreamIdentifier.Regex)) {
				var indexName = filter.StreamIdentifier.Prefix.FirstOrDefault(SystemStreams.IsIndexStream);
				var pool = httpContext.RequestServices.GetRequiredService<DuckDBConnectionPool>();
				if (indexName != null) {
					return filter.StreamIdentifier.Prefix.Count > 1
						? throw RpcExceptions.InvalidArgument("Index reads only work with one index name and cannot be combined with stream prefixes or other indexes")
						: getIndexEnumerator(indexName, pool);
				}
			}

			return getReadAllEnumerator(filter);
		}

		IAsyncEnumerator<ReadResponse> GetReadAllForwardsFilteredEnumerator() =>
			GetFilterOrIndexEnumerator(
				(indexName, pool) => new Enumerator.ReadIndexForwards(
					_publisher, indexName, request.Options.All.ToPosition(),
					request.Options.Count, user, requiresLeader, _expiryStrategy, pool, cancellationToken
				),
				filter => new Enumerator.ReadAllForwardsFiltered(
					_publisher,
					request.Options.All.ToPosition(),
					request.Options.Count,
					request.Options.ResolveLinks,
					ConvertToEventFilter(true, filter),
					user,
					requiresLeader,
					ConvertToWindow(filter),
					_expiryStrategy,
					cancellationToken)
			);

		IAsyncEnumerator<ReadResponse> GetReadAllBackwardsFilteredEnumerator() =>
			GetFilterOrIndexEnumerator(
				(indexName, pool) => new Enumerator.ReadIndexBackwards(
					_publisher, indexName, request.Options.All.ToPosition(),
					request.Options.Count, user, requiresLeader, _expiryStrategy, pool, cancellationToken
				),
				filter => new Enumerator.ReadAllBackwardsFiltered(
					_publisher,
					request.Options.All.ToPosition(),
					request.Options.Count,
					request.Options.ResolveLinks,
					ConvertToEventFilter(true, filter),
					user,
					requiresLeader,
					ConvertToWindow(filter),
					_expiryStrategy,
					cancellationToken)
			);

		IAsyncEnumerator<ReadResponse> GetAllSubscriptionFilteredEnumerator() =>
			GetFilterOrIndexEnumerator(
				(indexName, pool) => new Enumerator.IndexSubscription(
					_publisher, _expiryStrategy, request.Options.All.ToSubscriptionPosition(),
					indexName, user, requiresLeader, pool, cancellationToken
				),
				filter => new Enumerator.AllSubscriptionFiltered(
					_publisher,
					_expiryStrategy,
					request.Options.All.ToSubscriptionPosition(),
					request.Options.ResolveLinks,
					ConvertToEventFilter(true, request.Options.Filter),
					user,
					requiresLeader,
					ConvertToWindow(filter),
					request.Options.Filter.CheckpointIntervalMultiplier,
					cancellationToken)
			);

		static uint? ConvertToWindow(ReadReq.Types.Options.Types.FilterOptions filter) =>
			filter.WindowCase switch {
				ReadReq.Types.Options.Types.FilterOptions.WindowOneofCase.Count => null,
				ReadReq.Types.Options.Types.FilterOptions.WindowOneofCase.Max => filter.Max,
				_ => throw RpcExceptions.InvalidArgument(filter.WindowCase)
			};

		static IEventFilter ConvertToEventFilter(bool isAllStream, ReadReq.Types.Options.Types.FilterOptions filter) =>
			filter.FilterCase switch {
				ReadReq.Types.Options.Types.FilterOptions.FilterOneofCase.EventType => string.IsNullOrEmpty(filter.EventType.Regex)
					? EventFilter.EventType.Prefixes(isAllStream, filter.EventType.Prefix.ToArray())
					: EventFilter.EventType.Regex(isAllStream, filter.EventType.Regex),
				ReadReq.Types.Options.Types.FilterOptions.FilterOneofCase.StreamIdentifier => string.IsNullOrEmpty(filter.StreamIdentifier.Regex)
					? EventFilter.StreamName.Prefixes(isAllStream, filter.StreamIdentifier.Prefix.ToArray())
					: EventFilter.StreamName.Regex(isAllStream, filter.StreamIdentifier.Regex),
				_ => throw RpcExceptions.InvalidArgument(filter)
			};
	}
}

static class ResponseConverter {
	public static bool TryConvertReadResponse(ReadResponse readResponse, ReadReq.Types.Options.Types.UUIDOption uuidOption, out ReadResp readResp) {
		readResp = readResponse switch {
			ReadResponse.EventReceived eventReceived => new ReadResp {
				Event = ConvertToReadEvent(uuidOption, eventReceived.Event)
			},
			ReadResponse.SubscriptionConfirmed subscriptionConfirmed => new ReadResp {
				Confirmation = new SubscriptionConfirmation {
					SubscriptionId = subscriptionConfirmed.SubscriptionId
				}
			},
			ReadResponse.CheckpointReceived checkpointReceived => new ReadResp {
				Checkpoint = new Checkpoint {
					Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(checkpointReceived.Timestamp),
					CommitPosition = checkpointReceived.CommitPosition,
					PreparePosition = checkpointReceived.PreparePosition
				}
			},
			ReadResponse.StreamNotFound streamNotFound => new ReadResp {
				StreamNotFound = new StreamNotFound {
					StreamIdentifier = streamNotFound.StreamName
				}
			},
			ReadResponse.SubscriptionCaughtUp caughtUp => Convert(caughtUp),
			ReadResponse.SubscriptionFellBehind => null, // currently not sent to clients
			ReadResponse.LastStreamPositionReceived lastStreamPositionReceived => new ReadResp {
				LastStreamPosition = lastStreamPositionReceived.LastStreamPosition
			},
			ReadResponse.FirstStreamPositionReceived firstStreamPositionReceived => new ReadResp {
				FirstStreamPosition = firstStreamPositionReceived.FirstStreamPosition
			},
			_ => throw new ArgumentException($"Unknown read response type: {readResponse.GetType().Name}", nameof(readResponse))
		};

		return readResp != null;
	}

	private static ReadResp Convert(ReadResponse.SubscriptionCaughtUp caughtUp) {
		var response = new ReadResp {
			CaughtUp = new CaughtUp {
				Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(caughtUp.Timestamp),
			},
		};

		if (caughtUp.StreamCheckpoint is { } streamCheckpoint && streamCheckpoint >= 0) {
			response.CaughtUp.StreamRevision = streamCheckpoint;
		}

		if (caughtUp.AllCheckpoint is { } allCheckpoint && allCheckpoint != TFPos.HeadOfTf) {
			var unsignedPosition = Position.FromInt64(allCheckpoint.CommitPosition, allCheckpoint.PreparePosition);
			response.CaughtUp.Position = new() {
				CommitPosition = unsignedPosition.CommitPosition,
				PreparePosition = unsignedPosition.PreparePosition,
			};
		}

		return response;
	}

	public static void ConvertReadResponseException(ReadResponseException readResponseEx) {
		switch (readResponseEx) {
			case ReadResponseException.NotHandled.ServerNotReady:
				throw RpcExceptions.ServerNotReady();
			case ReadResponseException.NotHandled.ServerBusy:
				throw RpcExceptions.ServerBusy();
			case ReadResponseException.NotHandled.LeaderInfo leaderInfo:
				throw RpcExceptions.LeaderInfo(leaderInfo.Host, leaderInfo.Port);
			case ReadResponseException.NotHandled.NoLeaderInfo:
				throw RpcExceptions.NoLeaderInfo();
			case ReadResponseException.StreamDeleted streamDeleted:
				throw RpcExceptions.StreamDeleted(streamDeleted.StreamName);
			case ReadResponseException.AccessDenied:
				throw RpcExceptions.AccessDenied();
			case ReadResponseException.Timeout timeout:
				throw RpcExceptions.Timeout(timeout.ErrorMessage);
			case ReadResponseException.InvalidPosition:
				throw RpcExceptions.InvalidPositionException();
			case ReadResponseException.IndexNotFound indexNotFound:
				throw RpcExceptions.IndexNotFound(indexNotFound.IndexName);
			case ReadResponseException.UnknownMessage unknownMessage:
				throw RpcExceptions.UnknownMessage(unknownMessage.UnknownMessageType, unknownMessage.ExpectedMessageType);
			case ReadResponseException.UnknownError unknown:
				throw RpcExceptions.UnknownError(unknown.ResultType, unknown.Result, unknown.ErrorMessage);
			default:
				throw new ArgumentException($"Unknown read response exception type: {readResponseEx.GetType().Name}", nameof(readResponseEx));
		}
	}

	private static ReadEvent.Types.RecordedEvent ConvertToRecordedEvent(
		ReadReq.Types.Options.Types.UUIDOption uuidOption, EventRecord e, long? commitPosition,
		long? preparePosition) {
		if (e == null)
			return null;
		var position = Position.FromInt64(commitPosition ?? -1, preparePosition ?? -1);

		var result = new ReadEvent.Types.RecordedEvent {
			Id = uuidOption.ContentCase switch {
				ReadReq.Types.Options.Types.UUIDOption.ContentOneofCase.String => new UUID {
					String = e.EventId.ToString()
				},
				_ => Uuid.FromGuid(e.EventId).ToDto()
			},
			StreamIdentifier = e.EventStreamId,
			StreamRevision = StreamRevision.FromInt64(e.EventNumber),
			CommitPosition = position.CommitPosition,
			PreparePosition = position.PreparePosition,
			Data = ByteString.CopyFrom(e.Data.Span),
			CustomMetadata = ByteString.CopyFrom(e.Metadata.Span)
		};
		result.Metadata.AddGrpcMetadataFrom(e);
		return result;
	}

	private static ReadEvent ConvertToReadEvent(ReadReq.Types.Options.Types.UUIDOption uuidOption, ResolvedEvent e) {
		var readEvent = new ReadEvent {
			Link = ConvertToRecordedEvent(uuidOption, e.Link, e.LinkPosition?.CommitPosition,
				e.LinkPosition?.PreparePosition),
			Event = ConvertToRecordedEvent(uuidOption, e.Event, e.EventPosition?.CommitPosition,
				e.EventPosition?.PreparePosition),
		};
		if (e.OriginalPosition.HasValue) {
			var position = Position.FromInt64(
				e.OriginalPosition.Value.CommitPosition,
				e.OriginalPosition.Value.PreparePosition);
			readEvent.CommitPosition = position.CommitPosition;
		} else {
			readEvent.NoPosition = new Empty();
		}

		return readEvent;
	}
}
