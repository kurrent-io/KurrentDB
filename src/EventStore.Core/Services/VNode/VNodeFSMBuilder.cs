// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Runtime;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.VNode;

/// <summary>
/// Builder syntax for constructing <see cref="VNodeFSM"/> in the code
/// </summary>
public sealed class VNodeFSMBuilder {
	private readonly ReadOnlyValueReference<VNodeState> _stateRef;

	// The dictionary keeps a mapping between concrete message typeof(T) type and its handler
	// in the form of Func<T, CancellationToken, ValueTask> delegate instance where T <= Message.
	// The mapping cannot be expressed at language level in type-safe manner, so we're using a common denominator
	// for all Func<T, CancellationToken, ValueTask> variations: MulticastDelegate
	private readonly Dictionary<Type, MulticastDelegate>[] _handlers;
	private readonly Func<Message, CancellationToken, ValueTask>[] _defaultHandlers;

	public VNodeFSMBuilder(ReadOnlyValueReference<VNodeState> stateRef) {
		_stateRef = stateRef;

		var maxState = (int)Enum.GetValues<VNodeState>().Max();
		_handlers = new Dictionary<Type, MulticastDelegate>[maxState + 1];
		_defaultHandlers = new Func<Message, CancellationToken, ValueTask>[maxState + 1];
	}

	internal void AddHandler<TActualMessage>(VNodeState state, Func<TActualMessage, CancellationToken, ValueTask> handler)
		where TActualMessage : Message {
		var stateHandlers = _handlers[(int)state] ??= new();

		// Perf: unsafe reinterpret cast is valid here because VNodeFSM routes the message by its type
		if (!stateHandlers.TryAdd(typeof(TActualMessage), Unsafe.As<Action<Message>>(handler))) {
			throw new InvalidOperationException(
				$"Handler already defined for state {state} and message {typeof(TActualMessage).FullName}");
		}
	}

	internal void AddDefaultHandler(VNodeState state, Func<Message, CancellationToken, ValueTask> handler) {
		ref var defaultHandler = ref _defaultHandlers[(int)state];
		if (defaultHandler is not null)
			throw new InvalidOperationException($"Default handler already defined for state {state}");

		defaultHandler = handler;
	}

	public VNodeFSMStatesDefinition InAnyState() => InStates(Enum.GetValues<VNodeState>().Distinct().ToArray());

	public VNodeFSMStatesDefinition InState(VNodeState state) => InStates(state);

	public VNodeFSMStatesDefinition InStates(params VNodeState[] states) {
		return new VNodeFSMStatesDefinition(this, states);
	}

	public VNodeFSMStatesDefinition InAllStatesExcept(params VNodeState[] states) {
		Ensure.Positive(states.Length, "states.Length");

		var s = Enum.GetValues<VNodeState>().Except(states).Distinct().ToArray();
		return new VNodeFSMStatesDefinition(this, s);
	}

	public VNodeFSM Build() => new(_stateRef, _handlers, _defaultHandlers);
}
