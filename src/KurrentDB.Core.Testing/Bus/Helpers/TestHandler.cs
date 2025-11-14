// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Tests.Bus.Helpers;

public class TestHandler<T> : IHandle<T> where T : Message {
	public readonly List<T> HandledMessages = new();
	private readonly List<(Type type, Func<Message, T> func)> _converters = new();

	public void Handle(T message) {
		var converter = _converters.FirstOrDefault(
			converter => converter.type.IsAssignableFrom(message.GetType()));

		if (converter != default) {
			HandledMessages.Add(converter.func(message));
			return;
		}

		HandledMessages.Add(message);
	}

	public void AddConverter(Type type, Func<Message, T> converter) {
		_converters.Add((type, converter));
	}
}
