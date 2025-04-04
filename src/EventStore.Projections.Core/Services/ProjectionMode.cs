// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services;

public enum ProjectionMode {
	Transient = 0,
	OneTime = 1,

	//____1 = 1,
	//____2 = 2,
	//____3 = 3,
	Continuous = 4,
	AllNonTransient = 999,
}
