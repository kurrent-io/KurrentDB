// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.KontrolPlane;

public sealed class LeadershipRequiredException(Exception? e = null) : InvalidOperationException("Current Kontroller is not leader", e);
