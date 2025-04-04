// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Messaging;

namespace EventStore.Core.Tests.AwakeService;

public class TestMessage : Message {
	public readonly int Kind;

	public TestMessage(int kind) {
		Kind = kind;
	}
}
