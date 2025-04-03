// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Common.Utils;
using EventStore.Core.Helpers;
using EventStore.Core.Messaging;

namespace EventStore.Core.Bus;

public sealed class AdHocHandler<T>(Func<T, CancellationToken, ValueTask> handle) : IAsyncHandle<T> where T : Message {
	public AdHocHandler(Action<T> handle) : this(handle.ToAsync()) {
	}

	ValueTask IAsyncHandle<T>.HandleAsync(T message, CancellationToken token) => handle.Invoke(message, token);
}

public struct AdHocHandlerStruct<T> : IHandle<T>, IHandleTimeout where T : Message {
	private readonly Action<T> _handle;
	private readonly Action _timeout;

	public AdHocHandlerStruct(Action<T> handle, Action timeout) {
		Ensure.NotNull(handle, "handle");

		HandlesTimeout = timeout is not null;
		_handle = handle;
		_timeout = timeout.OrNoOp();
	}

	public bool HandlesTimeout { get; }

	public void Handle(T response) {
		_handle(response);
	}

	public void Timeout() {
		_timeout();
	}
}
