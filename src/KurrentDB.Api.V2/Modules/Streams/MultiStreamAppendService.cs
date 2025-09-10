// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

// ReSharper disable MethodHasAsyncOverload

using System.Collections.Immutable;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Api.Errors;
using KurrentDB.Api.Infrastructure;
using KurrentDB.Api.Streams.Authorization;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Protocol.V2.Streams;

using static System.Threading.Tasks.TaskCreationOptions;
using static KurrentDB.Core.Messages.ClientMessage;
using static KurrentDB.Core.Messages.ClientMessage.NotHandled.Types;
using static KurrentDB.Protocol.V2.Streams.StreamsService;

namespace KurrentDB.Api.Streams;

public record StreamsServiceOptions {
	public int MaxAppendSize { get; init; } = TFConsts.ChunkSize;
	public int MaxRecordSize { get; init; } = TFConsts.EffectiveMaxLogRecordSize;
}

public class StreamsService : StreamsServiceBase {
	public StreamsService(
		StreamsServiceOptions options,
		IPublisher publisher,
		IAuthorizationProvider authz,
		IDurationTracker durationTracker,
		NodeSystemInfoProvider nodeInfo) {
		NodeInfo = nodeInfo;
		Options          = options;
		Publisher        = publisher;
		Authz            = authz;
		DurationTracker  = durationTracker;
	}

	StreamsServiceOptions  Options          { get; }
	IPublisher             Publisher        { get; }
	IAuthorizationProvider Authz            { get; }
	IDurationTracker       DurationTracker  { get; }
	NodeSystemInfoProvider NodeInfo { get; }

	// public override Task<MultiStreamAppendResponse> MultiStreamAppend(MultiStreamAppendRequest request, ServerCallContext context) =>
	// 	MultiStreamAppendCore(request.Input.ToAsyncEnumerable(), context);
	//
	// public override Task<MultiStreamAppendResponse> MultiStreamAppendSession(IAsyncStreamReader<AppendStreamRequest> requestStream, ServerCallContext context) =>
	// 	MultiStreamAppendCore(requestStream.ReadAllAsync(), context);

	class RequestTracker {
		public IndexedSet<AppendRequest>      Requests  { get; } = new(QuickAppendRequestEqualityComparer.Instance);
		public ImmutableArray<string>.Builder Streams   { get; } = ImmutableArray.CreateBuilder<string>();
		public ImmutableArray<long>.Builder   Revisions { get; } = ImmutableArray.CreateBuilder<long>();
		public ImmutableArray<Event>.Builder  Events    { get; } = ImmutableArray.CreateBuilder<Event>();
		public ImmutableArray<int>.Builder    Indexes   { get; } = ImmutableArray.CreateBuilder<int>();

		int TotalAppendSize { get; set; }

		int _index = -1;

		public RequestTracker Track(AppendRequest request) {
			// *** Temporary limitation ***
			if (!Requests.Add(request)) {

				throw ApiErrors.StreamAlreadyInAppendSession(request.Stream);
			}

			Streams.Add(request.Stream);
			Revisions.Add(request.HasExpectedRevision ? request.ExpectedRevision : ExpectedVersion.Any);

			foreach (var record in request.Records) {
				record.PrepareRecord(TimeProvider.System).EnsureValid();

				record.CalculateSizeOnDisk(out var report);

				if (report.RecordTooBig)
					throw ApiErrors.AppendRecordSizeExceeded(request.Stream, record.RecordId, report.TotalSize, TFConsts.EffectiveMaxLogRecordSize);

				var evnt = record.MapToEvent();

				if ((TotalAppendSize += report.TotalSize) > TFConsts.ChunkSize)
					throw ApiErrors.AppendTransactionSizeExceeded(TFConsts.ChunkSize);

				Indexes.Add(_index++);
				Events.Add(evnt);
			}

			return this;
		}

		sealed class QuickAppendRequestEqualityComparer : IEqualityComparer<AppendRequest> {
			public static readonly QuickAppendRequestEqualityComparer Instance = new();

			public bool Equals(AppendRequest? x, AppendRequest? y) =>
				string.Equals(x?.Stream, y?.Stream, StringComparison.OrdinalIgnoreCase);

			public int GetHashCode(AppendRequest obj) =>
				StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream);

			// 	public bool Equals(AppendRequest? x, AppendRequest? y) =>
			// 		ReferenceEquals(x, y) || x is not null && y is not null && x.GetType() == y.GetType() &&
			// 		string.Equals(x.Stream, y.Stream, StringComparison.OrdinalIgnoreCase);
			//
			// 	public int GetHashCode(AppendRequest obj) =>
			// 		obj.Stream != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Stream) : 0;
		}
	}

	public override Task<AppendSessionResponse> AppendSession(IAsyncStreamReader<AppendRequest> incomingRequests, ServerCallContext context) =>
		AppendRecords(incomingRequests.ReadAllAsync(), context);

	async Task<AppendSessionResponse> AppendRecords(IAsyncEnumerable<AppendRequest> incomingRequests, ServerCallContext context) {
		using var duration = DurationTracker.Start(); // TODO SS: move out to interceptor soon

		await EnsureNodeIsLeader(context);

		try {
			var user = context.GetHttpContext().User;

			// var tracker = new RequestTracker();
			// await foreach (var request in incomingRequests) {
			// 	AppendRequestValidator.Instance.ValidateAndThrow(request);
			// 	await _authz.AuthorizeStreamOperation(request.Stream, StreamOperation.Write, user, context.CancellationToken);
			// 	tracker.Track(request);
			// }

			var tracker = await incomingRequests
				.AggregateAwaitWithCancellationAsync(new RequestTracker(), async (trckr, req, ct) => {
					await Authz.AuthorizeStreamOperation(stream: req.Stream, operation: StreamOperation.Write, user: user, ct: ct);
					return trckr.Track(req);
				}, context.CancellationToken);

			var callback = new AppendRecordsCallback(idx => tracker.Requests[idx]);

			var writeEventsCommand = new WriteEvents(
				callback,
				tracker.Streams.ToImmutable(),
				tracker.Revisions.ToImmutable(),
				tracker.Events.ToImmutable(),
				tracker.Indexes.ToImmutable(),
				user,
				context.CancellationToken
			);

			Publisher.Publish(writeEventsCommand);

			return await callback.WaitForReply;
		}
		catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}

	class AppendRecordsCallback(Func<int, AppendRequest> getRequestById) : IEnvelope {
		TaskCompletionSource<AppendSessionResponse> Operation { get; } = new(RunContinuationsAsynchronously);

		public Task<AppendSessionResponse> WaitForReply => Operation.Task;

		public void ReplyWith<T>(T message) where T : Message {
			try {
				if (message is WriteEventsCompleted { Result: OperationResult.Success } success)
					Operation.TrySetResult(MapToResult(success, getRequestById));
				else
					Operation.TrySetException(MapToError(message, getRequestById));
			} catch (Exception ex) {
				// fallback to ensure we never leave the client hanging
				Operation.TrySetException(ex);
			}
		}

		static AppendSessionResponse MapToResult(WriteEventsCompleted completed, Func<int, AppendRequest> getRequestById) {
			var output = new List<AppendResponse>();

			for (var i = 0; i < completed.LastEventNumbers.Length; i++) {
				output.Add(new() {
					Stream         = getRequestById(i).Stream,
					StreamRevision = completed.LastEventNumbers.Span[i]
				});
			}

			return new() {
				Output   = { output },
				Position = completed.CommitPosition
			};
		}

		static RpcException MapToError(Message message, Func<int, AppendRequest> getRequestById) {
			return message switch {
				WriteEventsCompleted completed => OnCompleted(completed),
				NotHandled notHandled          => OnNotHandled(notHandled),
				// _                              => ApiExceptions.UnexpectedCallbackResult<WriteEventsCompleted>(unexpectedResult: message)
			};

			RpcException OnCompleted(WriteEventsCompleted completed) => completed.Result switch {
				OperationResult.StreamDeleted  => ApiErrors.StreamDeleted(getRequestById(completed.FailureStreamIndexes.Span[0]).Stream),
				OperationResult.PrepareTimeout => ApiErrors.OperationTimeout(completed.Message),
				OperationResult.CommitTimeout  => ApiErrors.OperationTimeout(completed.Message),

				// TODO SS: consider returning all StreamRevisionConflict in one go intead of the first one
				OperationResult.WrongExpectedVersion => ApiErrors.StreamRevisionConflict(
					stream: getRequestById(completed.FailureStreamIndexes.Span[0]).Stream,
					expectedRevision: getRequestById(completed.FailureStreamIndexes.Span[0]).ExpectedRevision,
					actualRevision: completed.FailureCurrentVersions.Span[0]
				),

				// _ => ApiExceptions.InternalServerError($"Append request completed in error with unexpected result: {completed.Result}")

				// OperationResult.InvalidTransaction is not possible here as we validate the max append size while receiving the requests
				// OperationResult.ForwardTimeout is not possible here as we set requireLeader=true on the request
				// OperationResult.PrepareTimeout is not possible here as we don't have old explicit transactions
				// OperationResult.AccessDenied is not possible here as we check authorization before starting the operation
			};

			RpcException OnNotHandled(NotHandled notHandled) => notHandled.Reason switch {
				NotHandledReason.NotReady => ApiErrors.ServerNotReady(),
				NotHandledReason.TooBusy  => ApiErrors.ServerOverloaded(),

				//_ => ApiExceptions.InternalServerError($"Append request not handled with unexpected reason: {notHandled.Reason}"),

				// NotHandledReason.NotLeader is not possible here as we set requireLeader=true on the request
				// NotHandledReason.IsReadOnly is not possible here as we set requireLeader=true on the request
			};
		}
	}

	async ValueTask EnsureNodeIsLeader(ServerCallContext ctx) {
		var info = await NodeInfo.CheckLeadership(ctx.CancellationToken);
		if (info.IsNotLeader)
			throw ApiErrors.NotLeaderNode(info.InstanceId, info.Endpoint);

		// // what matters is if we are the leader or not
		// // or do we do this? I dont like it at all, and I dont care about the headers being there
		// if (ctx.RequestHeaders.Get(Constants.Headers.RequiresLeader) is not null) {
		// 	const string message = $"All writes must go to the leader. "
		// 	                     + $"The '{Constants.Headers.RequiresLeader}' header is no longer supported "
		// 	                     + $"on this API, because writing to a follower to have it forward to the leader "
		// 	                     + $"is just a way to accidentally slow down your writes.";
		//
		// 	throw ApiExceptions.InvalidRequest("RequestHeaders", message);
		// }
	}
}
