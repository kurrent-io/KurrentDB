// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Messaging;
using KurrentDB.Projections.Core.Messages;

namespace KurrentDB.Projections.Core.Messaging;

[DerivedMessage(ProjectionMessage.Misc)]
public partial class UnwrapEnvelopeMessage : Message {
	private readonly Action _action;

	public UnwrapEnvelopeMessage(Action action) {
		_action = action;
	}

	public Action Action {
		get { return _action; }
	}
}
