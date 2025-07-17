// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Protocol.V2;
using static System.Threading.Tasks.TaskCreationOptions;

namespace KurrentDB.Core.Services.Transport.Grpc.V2;

public class MultiStreamAppendService : StreamsService.StreamsServiceBase {
	readonly IPublisher _publisher;
	readonly IAuthorizationProvider _authorizationProvider;
	readonly IDurationTracker _appendTracker;
	readonly MultiStreamAppendConverter _converter;

	static readonly Operation WriteOperation = new(Operations.Streams.Write);
	static readonly ErrorDetails.Types.AccessDenied AccessDenied = new();

	public MultiStreamAppendService(
		IPublisher publisher,
		IAuthorizationProvider authorizationProvider,
		IDurationTracker appendTracker,
		int maxAppendSize,
		int maxAppendEventSize,
		int chunkSize) {
		_publisher = publisher;
		_authorizationProvider = authorizationProvider;
		_appendTracker = appendTracker;
		_converter = new(chunkSize, maxAppendSize, maxAppendEventSize);
	}

	public override Task<MultiStreamAppendResponse> MultiStreamAppend(MultiStreamAppendRequest request, ServerCallContext context) =>
		MultiStreamAppendCore(request.Input.ToAsyncEnumerable(), context);

	public override Task<MultiStreamAppendResponse> MultiStreamAppendSession(IAsyncStreamReader<AppendStreamRequest> requestStream, ServerCallContext context) =>
		MultiStreamAppendCore(requestStream.ReadAllAsync(), context);

	async Task<MultiStreamAppendResponse> MultiStreamAppendCore(IAsyncEnumerable<AppendStreamRequest> requestStream, ServerCallContext context) {
		using var duration = _appendTracker.Start();
		try {
			EnsureLeaderConstraint(context);

			var user = context.GetHttpContext().User;
			var requests = new List<AppendStreamRequest>();
			var seenStreams = new HashSet<string>();

			await foreach (var request in requestStream) {
				if (request.Records is [])
					throw RpcExceptions.InvalidArgument($"Write to stream '{request.Stream}' does not have any records");

				// temporary limitation
				if (!seenStreams.Add(request.Stream))
					throw RpcExceptions.InvalidArgument("Two AppendStreamRequests for one stream is not currently supported: " +
														$"'{request.Stream}' is already in the request list");

				var accessGranted = await CheckStreamAccess(request.Stream);
				if (!accessGranted) {
					var failure = new AppendStreamFailure {
						Stream = request.Stream,
						AccessDenied = AccessDenied,
					};
					return new() { Failure = new() { Output = { failure } } }; // fail fast if any request is unauthorized
				}

				requests.Add(request);
			}

			var convertedEvents = _converter.ConvertToEvents(requests);

			var completionSource = new TaskCompletionSource<MultiStreamAppendResponse>(RunContinuationsAsynchronously);

			var envelope = CallbackEnvelope.Create(
				(Converter: _converter, Source: completionSource, Requests: requests),
				static (state, msg) => {
					try {
						state.Source.TrySetResult(state.Converter.ConvertToResponse(msg, state.Requests));
					} catch (Exception ex) {
						state.Source.TrySetException(ex);
					}
				});

			var correlationId = Guid.NewGuid();

			var writeEventsCommand = new ClientMessage.WriteEvents(
				internalCorrId: correlationId,
				correlationId: correlationId,
				envelope: envelope,
				requireLeader: true,
				eventStreamIds: convertedEvents.StreamIds,
				expectedVersions: convertedEvents.ExpectedVersions,
				events: convertedEvents.Events,
				eventStreamIndexes: convertedEvents.StreamIndexes,
				user: user,
				cancellationToken: context.CancellationToken
			);

			_publisher.Publish(writeEventsCommand);

			return await completionSource.Task;

			ValueTask<bool> CheckStreamAccess(string stream) {
				var op = WriteOperation.WithParameter(Operations.Streams.Parameters.StreamId(stream));
				return _authorizationProvider.CheckAccessAsync(user, op, context.CancellationToken);
			}
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}

	/// <summary>
	/// Verifies if the leader constraint is satisfied for the incoming request context and throws an exception if it is not.
	/// <remarks>
	/// It is not supported because writing to a follower to have it forward to the leader is just
	/// a way to accidentally slow down your writes.
	/// </remarks>
	/// </summary>
	static void EnsureLeaderConstraint(ServerCallContext context) {
		if (context.RequestHeaders.Get(Constants.Headers.RequiresLeader) is not null)
			throw RpcExceptions.InvalidArgument("requires-leader is not supported on this API");
	}
}
