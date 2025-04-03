// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Bus;

namespace EventStore.Core.Messaging;

public static class RequestClient {
	public static Task<TResponse> RequestAsync<TRequest, TResponse>(IPublisher publisher, Func<IEnvelope, TRequest> getRequest, CancellationToken cancellationToken)
		where TRequest : Message where TResponse : Message {
		var envelope = new TcsEnvelope<TResponse>();
		var request = getRequest(envelope);
		publisher.Publish(request);
		return envelope.Task.WaitAsync(cancellationToken);
	}
}
