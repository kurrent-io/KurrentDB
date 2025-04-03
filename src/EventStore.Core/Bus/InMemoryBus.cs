// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Diagnostics;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using ILogger = Serilog.ILogger;

namespace EventStore.Core.Bus;

/// <summary>
/// Synchronously dispatches messages to zero or more subscribers.
/// Subscribers are responsible for handling exceptions
/// </summary>
public partial class InMemoryBus : ISubscriber, IAsyncHandle<Message> {
	public static InMemoryBus CreateTest(bool watchSlowMsg = true) =>
		new("Test", watchSlowMsg);

	public static readonly TimeSpan DefaultSlowMessageThreshold = TimeSpan.FromMilliseconds(48);
	private static readonly ILogger Log = Serilog.Log.ForContext<InMemoryBus>();

	private readonly FrozenDictionary<Type, MessageTypeHandler> _handlers;
	private readonly double _slowMsgThresholdMs;

	public InMemoryBus(string name, bool watchSlowMsg = true, TimeSpan? slowMsgThreshold = null) {
		_handlers = CreateMessageTypeHandlers();
		Name = name;

		if (watchSlowMsg)
			_slowMsgThresholdMs = slowMsgThreshold.GetValueOrDefault(DefaultSlowMessageThreshold).TotalMilliseconds;
	}

	public string Name { get; }

	public void Subscribe<T>(IAsyncHandle<T> handler) where T : Message {
		ArgumentNullException.ThrowIfNull(handler);

		if (!_handlers.TryGetValue(typeof(T), out var handlers))
			throw new GenericArgumentException<T>("Unexpected message type", nameof(handler));

		Debug.Assert(handlers is MessageTypeHandler<T>);
		Unsafe.As<MessageTypeHandler<T>>(handlers).AddHandler(handler);
	}

	public void Unsubscribe<T>(IAsyncHandle<T> handler) where T : Message {
		ArgumentNullException.ThrowIfNull(handler);

		if (!_handlers.TryGetValue(typeof(T), out var handlers))
			throw new GenericArgumentException<T>("Unexpected message type", nameof(handler));

		Debug.Assert(handlers is MessageTypeHandler<T>);
		Unsafe.As<MessageTypeHandler<T>>(handlers).RemoveHandler(handler);
	}

	private bool IsSlowMsgWatchEnabled => BitConverter.DoubleToInt64Bits(_slowMsgThresholdMs) is not 0L;

	public ValueTask DispatchAsync(Message message, CancellationToken token = default) {
		if (message is null)
			return ValueTask.FromException(new ArgumentNullException(nameof(message)));

		if (!_handlers.TryGetValue(message.GetType(), out var handlers))
			return ValueTask.FromException(new ArgumentOutOfRangeException(nameof(message), "Unexpected message type"));

		// Perf: branching with single if-else statement is better than virtual dispatch
		return IsSlowMsgWatchEnabled
			? DispatchAndWatchSlowMsg(handlers, message, token)
			: handlers.InvokeAsync(message, token);
	}

	private async ValueTask DispatchAndWatchSlowMsg(MessageTypeHandler handlers, Message message,
		CancellationToken token) {
		var ts = new Timestamp();

		await handlers.InvokeAsync(message, token);

		var elapsedMs = ts.ElapsedMilliseconds;
		if (elapsedMs > _slowMsgThresholdMs) {
			Log.Debug("SLOW BUS MSG [{bus}]: {message} - {elapsed}ms.",
				Name, message.GetType().Name, (int)elapsedMs);
			if (elapsedMs > QueuedHandlerThreadPool.VerySlowMsgThreshold.TotalMilliseconds &&
			    message is not SystemMessage.SystemInit)
				Log.Error("---!!! VERY SLOW BUS MSG [{bus}]: {message} - {elapsed}ms.",
					Name, message.GetType().Name, (int)elapsedMs);
		}
	}

	ValueTask IAsyncHandle<Message>.HandleAsync(Message message, CancellationToken token)
		=> DispatchAsync(message, token);
}
