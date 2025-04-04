// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Data;

public class Epoch {
	public readonly long EpochPosition;
	public readonly int EpochNumber;
	public readonly Guid EpochId;

	public Epoch(long epochPosition, int epochNumber, Guid epochId) {
		EpochPosition = epochPosition;
		EpochNumber = epochNumber;
		EpochId = epochId;
	}
}
