// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Time;

public interface IClock {
	Instant Now { get; }
	long SecondsSinceEpoch { get; }
}

public class Clock : IClock {
	public static Clock Instance { get; } = new();
	private Clock() { }
	public Instant Now => Instant.Now;
	public long SecondsSinceEpoch => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
