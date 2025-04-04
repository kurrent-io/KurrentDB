// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.Transport.Http.Messages;
using Microsoft.AspNetCore.Http;

namespace EventStore.Core.Services.Transport.Http;

public class InternalDispatcherEndpoint(IPublisher inputBus, MultiQueuedHandler requestsMultiHandler) : IHandle<HttpMessage.PurgeTimedOutRequests> {
	private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
	private readonly IEnvelope _publishEnvelope = inputBus;

	public void Handle(HttpMessage.PurgeTimedOutRequests message) {
		requestsMultiHandler.PublishToAll(message);
		inputBus.Publish(TimerMessage.Schedule.Create(UpdateInterval, _publishEnvelope, message));
	}

	public Task InvokeAsync(HttpContext context, RequestDelegate next) {
		if (context.IsGrpc() || context.Request.Path.StartsWithSegments("/ui"))
			return next(context);

		if (!InternalHttpHelper.TryGetInternalContext(context, out var manager, out var match, out var tcs)) {
			return next(context);
		}

		requestsMultiHandler.Publish(new AuthenticatedHttpRequestMessage(manager, match));
		return tcs.Task;
	}
}
