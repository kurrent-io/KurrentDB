// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core.Messages;
using KurrentDB.Core.Bus;

namespace KurrentDB.TcpPlugin.Tests;

public class TcpMessageCollector : IHandle<ClientMessage.ReadEvent> {
	private TaskCompletionSource<ClientMessage.ReadEvent> _source = new();

	public Task<ClientMessage.ReadEvent> Message => _source.Task;
	public void Handle(ClientMessage.ReadEvent message) {
		_source.TrySetResult(message);
	}
}
