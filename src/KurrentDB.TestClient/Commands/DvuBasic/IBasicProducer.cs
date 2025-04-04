// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

#pragma warning disable 1591

namespace KurrentDB.TestClient.Commands.DvuBasic;

public interface IBasicProducer {
	string Name { get; }

	Event Create(int version);
	bool Equal(int eventVersion, string eventType, byte[] actualData);
}
