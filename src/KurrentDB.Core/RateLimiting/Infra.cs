// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Core.RateLimiting;

public class TooBusyException() : Exception;

public readonly record struct Resource(Source Source, PriorityEx Priority) : ITupleResource<Source, PriorityEx> {
	public Source Item1 => Source;
	public PriorityEx Item2 => Priority;
}

public interface ITupleResource<T, U> {
	T Item1 { get; }
	U Item2 { get; }
}

public enum Source {
	None,
	Index,
	Archive,
	// Archive cache?
	ChunkCache,
	FileSystem,
}

public enum Priority {
	None,
	Low,
	Medium,
	High,
}

//qq name, used to be PriorityPair still could be better
public record struct PriorityEx(Priority Priority, bool Continued);

