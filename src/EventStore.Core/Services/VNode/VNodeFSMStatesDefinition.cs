// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;
using EventStore.Core.Data;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.VNode;

public readonly ref struct VNodeFSMStatesDefinition {
	internal readonly VNodeFSMBuilder FSM;
	internal readonly ReadOnlySpan<VNodeState> States;

	internal VNodeFSMStatesDefinition(VNodeFSMBuilder fsm, ReadOnlySpan<VNodeState> states) {
		FSM = fsm;
		States = states;
	}

	public VNodeFSMHandling<TMessage> When<TMessage>() where TMessage : Message {
		return new VNodeFSMHandling<TMessage>(this, defaultHandler: false);
	}

	public VNodeFSMHandling<Message> WhenOther() {
		return new VNodeFSMHandling<Message>(this, defaultHandler: true);
	}

	public VNodeFSMStatesDefinition InAnyState() {
		return FSM.InAnyState();
	}

	public VNodeFSMStatesDefinition InState(VNodeState state) {
		return FSM.InState(state);
	}

	public VNodeFSMStatesDefinition InStates(params VNodeState[] states) {
		return FSM.InStates(states);
	}

	public VNodeFSMStatesDefinition InAllStatesExcept(params VNodeState[] states) {
		Ensure.Positive(states.Length, "states.Length");
		return FSM.InAllStatesExcept(states);
	}

	public VNodeFSM Build() {
		return FSM.Build();
	}
}
