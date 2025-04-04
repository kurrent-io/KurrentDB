// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Client;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Plugins.Authorization;
using Grpc.Core;

namespace EventStore.Core.Services.Transport.Grpc;

internal partial class PersistentSubscriptions {
	private static readonly Operation RestartOperation = new Operation(Plugins.Authorization.Operations.Subscriptions.Restart);

	public override async Task<Empty> RestartSubsystem(Empty request, ServerCallContext context) {
		var restartSubsystemSource = new TaskCompletionSource<Empty>();

		var user = context.GetHttpContext().User;

		if (!await _authorizationProvider.CheckAccessAsync(user,
			RestartOperation, context.CancellationToken)) {
			throw RpcExceptions.AccessDenied();
		}

		_publisher.Publish(new SubscriptionMessage.PersistentSubscriptionsRestart(
			new CallbackEnvelope(HandleRestartSubsystemCompleted)));
		return await restartSubsystemSource.Task;

		void HandleRestartSubsystemCompleted(Message message) {
			if (message is ClientMessage.NotHandled notHandled &&
				RpcExceptions.TryHandleNotHandled(notHandled, out var ex)) {
				restartSubsystemSource.TrySetException(ex);
				return;
			}

			switch (message) {
				case SubscriptionMessage.PersistentSubscriptionsRestarting _:
					restartSubsystemSource.TrySetResult(new Empty());
					return;
				case SubscriptionMessage.InvalidPersistentSubscriptionsRestart fail:
					restartSubsystemSource.TrySetException(
						RpcExceptions.PersistentSubscriptionFailed("", "",
							$"Persistent Subscriptions cannot be restarted as it is in the wrong state."));
					return;
				default:
					restartSubsystemSource.TrySetException(
						RpcExceptions.UnknownMessage<SubscriptionMessage.PersistentSubscriptionsRestarting>(message));
					return;
			}
		}
	}
}
