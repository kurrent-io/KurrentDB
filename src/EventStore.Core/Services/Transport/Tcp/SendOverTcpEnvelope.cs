// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.Transport.Tcp;

public class SendOverTcpEnvelope(TcpConnectionManager manager, IPublisher networkSendQueue) : IEnvelope {
	public void ReplyWith<T>(T message) where T : Message {
		if (manager is { IsClosed: false }) {
			networkSendQueue.Publish(new TcpMessage.TcpSend(manager, message));
		}
	}
}
