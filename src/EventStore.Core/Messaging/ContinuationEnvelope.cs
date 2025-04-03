// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EventStore.Core.Messaging;

public class ChannelEnvelope : IEnvelope {
	private readonly Channel<Message> _channel;

	public ChannelEnvelope(Channel<Message> channel) {
		_channel = channel;
	}

	public void ReplyWith<T>(T message) where T : Message {
		_channel.Writer.TryWrite(message);
	}
}
public class ContinuationEnvelope : IEnvelope {
	private readonly Func<Message, CancellationToken, Task> _onMessage;
	private readonly SemaphoreSlim _semaphore;
	private readonly CancellationToken _cancellationToken;

	public ContinuationEnvelope(Func<Message, CancellationToken, Task> onMessage, SemaphoreSlim semaphore,
		CancellationToken cancellationToken) {
		_onMessage = onMessage;
		_semaphore = semaphore;
		_cancellationToken = cancellationToken;
	}

	public void ReplyWith<T>(T message) where T : Message {
		try {
			_semaphore.Wait(_cancellationToken);
			_onMessage(message, _cancellationToken).ContinueWith(_ => _semaphore.Release(), _cancellationToken);
		}
		catch (ObjectDisposedException) {}
		catch (OperationCanceledException) {}
	}
}
