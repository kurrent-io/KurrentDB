// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Concurrent;
using System.Net;
using EventStore.Common.Utils;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.Transport.Http;

public class HttpMessagePipe {
	private readonly ConcurrentDictionary<Type, IMessageSender> _senders = new();

	public void RegisterSender<T>(ISender<T> sender) where T : Message {
		Ensure.NotNull(sender);
		_senders.TryAdd(typeof(T), new MessageSender<T>(sender));
	}

	public void Push(Message message, IPEndPoint endPoint) {
		Ensure.NotNull(message);
		Ensure.NotNull(endPoint);

		var type = message.GetType();

		if (_senders.TryGetValue(type, out var sender))
			sender.Send(message, endPoint);
	}
}

public interface ISender<in T> where T : Message {
	void Send(T message, IPEndPoint endPoint);
}

public interface IMessageSender {
	void Send(Message message, IPEndPoint endPoint);
}

public class MessageSender<T> : IMessageSender where T : Message {
	private readonly ISender<T> _sender;

	public MessageSender(ISender<T> sender) {
		Ensure.NotNull(sender);
		_sender = sender;
	}

	public void Send(Message message, IPEndPoint endPoint) {
		Ensure.NotNull(message);
		Ensure.NotNull(endPoint);

		_sender.Send((T)message, endPoint);
	}
}
