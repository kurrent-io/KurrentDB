// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Metrics;
using KurrentDB.Protocol.V2;

namespace KurrentDB.Core.Services.Transport.Grpc;

public class MultiStreamAppendService : StreamsService.StreamsServiceBase {
	private readonly IPublisher _publisher;
	private readonly IAuthorizationProvider _authorizationProvider;
	private readonly IDurationTracker _appendTracker;
	private readonly MSARequestConverter _requestConverter;
	private readonly MSAResponseConverter _responseConverter;

	private static readonly Operation WriteOperation = new(Operations.Streams.Write);
	private static readonly ErrorDetails.Types.AccessDenied AccessDenied = new();

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
		_requestConverter = new(maxAppendSize, maxAppendEventSize);
		_responseConverter = new(chunkSize);
	}

	public override Task<MultiStreamAppendResponse> MultiStreamAppend(
		MultiStreamAppendRequest request,
		ServerCallContext context) {

		return MultiStreamAppendCore(request.Input, context);
	}

	public override async Task<MultiStreamAppendResponse> MultiStreamAppendSession(
		IAsyncStreamReader<AppendStreamRequest> requestStream,
		ServerCallContext context) {

		var appendStreamRequests = new List<AppendStreamRequest>();

		await foreach (var appendRequest in requestStream.ReadAllAsync()) {
			appendStreamRequests.Add(appendRequest);
		}

		return await MultiStreamAppendCore(appendStreamRequests, context);
	}

	async Task<MultiStreamAppendResponse> MultiStreamAppendCore(
		IReadOnlyList<AppendStreamRequest> appendStreamRequests,
		ServerCallContext context) {

		using var duration = _appendTracker.Start();
		try {
			var user = context.GetHttpContext().User;

			foreach (var appendRequest in appendStreamRequests) {
				var op = WriteOperation.WithParameter(Operations.Streams.Parameters.StreamId(appendRequest.Stream));
				if (!await _authorizationProvider.CheckAccessAsync(user, op, context.CancellationToken)) {
					return new MultiStreamAppendResponse {
						Failure = new() {
							Output = {
								new AppendStreamFailure {
									Stream = appendRequest.Stream,
									AccessDenied = AccessDenied,
								},
							},
						},
					};
				}
			}

			var appendResponseSource = new TaskCompletionSource<MultiStreamAppendResponse>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			var envelope = CallbackEnvelope.Create(
				(_responseConverter, appendResponseSource, appendStreamRequests),
				static (arg, msg) => {
					try {
						var result = arg._responseConverter.ConvertToMSAResponse(arg.appendStreamRequests, msg);
						arg.appendResponseSource.TrySetResult(result);
					} catch (Exception ex) {
						arg.appendResponseSource.TrySetException(ex);
					}
				});

			// not supported because writing to a follower to have it forward to the leader is just
			// a way to accidentally slow down your writes.
			if (context.RequestHeaders.Get(Constants.Headers.RequiresLeader) is not null) {
				throw new RpcException(new Status(
					StatusCode.InvalidArgument,
					"requires-leader is not supported on this API"));
			}

			var writeEvents = _requestConverter.ConvertRequests(
				appendStreamRequests: appendStreamRequests,
				envelope: envelope,
				user: user,
				token: context.CancellationToken);

			_publisher.Publish(writeEvents);

			return await appendResponseSource.Task;
		} catch (Exception ex) {
			duration.SetException(ex);
			throw;
		}
	}
}
