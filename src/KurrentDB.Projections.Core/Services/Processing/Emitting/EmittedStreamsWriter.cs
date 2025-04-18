// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using KurrentDB.Core.Data;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting;

public class EmittedStreamsWriter : IEmittedStreamsWriter {
	private IODispatcher _ioDispatcher;

	public EmittedStreamsWriter(IODispatcher ioDispatcher) {
		_ioDispatcher = ioDispatcher;
	}

	public void WriteEvents(string streamId, long expectedVersion, Event[] events, ClaimsPrincipal writeAs,
		Action<ClientMessage.WriteEventsCompleted> complete) {
		_ioDispatcher.WriteEvents(streamId, expectedVersion, events, writeAs, complete);
	}
}
