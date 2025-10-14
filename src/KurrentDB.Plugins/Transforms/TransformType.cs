// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Transforms;

public enum TransformType {
	Identity = 0,
	Encryption_AesGcm = 1,
}
