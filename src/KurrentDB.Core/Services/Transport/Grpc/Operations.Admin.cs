// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Client.Operations;
using EventStore.Plugins.Authorization;
using Grpc.Core;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.Transport.Grpc;
using Empty = EventStore.Client.Empty;

// ReSharper disable once CheckNamespace
namespace EventStore.Core.Services.Transport.Grpc;

internal partial class Operations {
	private static readonly Operation ShutdownOperation = new(Plugins.Authorization.Operations.Node.Shutdown);
	private static readonly Operation MergeIndexesOperation = new(Plugins.Authorization.Operations.Node.MergeIndexes);
	private static readonly Operation ResignOperation = new(Plugins.Authorization.Operations.Node.Resign);
	private static readonly Operation SetNodePriorityOperation = new(Plugins.Authorization.Operations.Node.SetPriority);
	private static readonly Operation RestartPersistentSubscriptionsOperation = new(Plugins.Authorization.Operations.Subscriptions.Restart);
	private static readonly Empty EmptyResult = new();

	public override async Task<Empty> Shutdown(Empty request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ShutdownOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new ClientMessage.RequestShutdown(exitProcess: true, shutdownHttp: true));
		return EmptyResult;
	}

	public override async Task<Empty> MergeIndexes(Empty request, ServerCallContext context) {
		var mergeResultSource = TaskCompletionSourceFactory.CreateDefault<string>();

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, MergeIndexesOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		var correlationId = Guid.NewGuid();
		_publisher.Publish(new ClientMessage.MergeIndexes(new CallbackEnvelope(OnMessage), correlationId, user));

		await mergeResultSource.Task;
		return EmptyResult;

		void OnMessage(Message message) {
			if (message is not ClientMessage.MergeIndexesResponse completed) {
				mergeResultSource.TrySetException(RpcExceptions.UnknownMessage<ClientMessage.MergeIndexesResponse>(message));
			} else {
				mergeResultSource.SetResult(completed.CorrelationId.ToString());
			}
		}
	}

	public override async Task<Empty> ResignNode(Empty request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, ResignOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new ClientMessage.ResignNode());
		return EmptyResult;
	}

	public override async Task<Empty> SetNodePriority(SetNodePriorityReq request, ServerCallContext context) {
		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, SetNodePriorityOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new ClientMessage.SetNodePriority(request.Priority));
		return EmptyResult;
	}

	public override async Task<Empty> RestartPersistentSubscriptions(Empty request, ServerCallContext context) {
		var restart = TaskCompletionSourceFactory.CreateDefault<bool>();
		var envelope = new CallbackEnvelope(OnMessage);

		var user = context.GetHttpContext().User;
		if (!await _authorizationProvider.CheckAccessAsync(user, RestartPersistentSubscriptionsOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new SubscriptionMessage.PersistentSubscriptionsRestart(envelope));

		await restart.Task;
		return new Empty();

		void OnMessage(Message message) {
			switch (message) {
				case SubscriptionMessage.PersistentSubscriptionsRestarting:
				case SubscriptionMessage.InvalidPersistentSubscriptionsRestart:
					restart.TrySetResult(true);
					break;
				default:
					restart.TrySetException(RpcExceptions.UnknownMessage<SubscriptionMessage.PersistentSubscriptionsRestarting>(message));
					break;
			}
		}
	}
}
