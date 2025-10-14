// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Tests.Bus.Helpers;

public class TestHandlerAndConverter<TInput, TOutput>(Func<TInput, TOutput> convert) :
	IHandle<TInput>
	where TInput : Message
	where TOutput : Message {

	public readonly List<TOutput> HandledMessages = new();

	public void Handle(TInput message) {
		HandledMessages.Add(convert(message));
	}
}
