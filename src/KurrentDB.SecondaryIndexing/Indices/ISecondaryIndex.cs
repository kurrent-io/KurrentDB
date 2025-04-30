// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;

namespace KurrentDB.SecondaryIndexing.Indices;

public interface ISecondaryIndex: IDisposable {
	ValueTask Init(CancellationToken ct);

	ValueTask<ulong?> GetLastPosition(CancellationToken ct);

	ValueTask<ulong?> GetLastSequence(CancellationToken ct);

	ISecondaryIndexProcessor Processor { get; }
}


[Flags]
public enum EventHandlingStatus : short {
	Ignored = 0b_1000,
	Success = 0b_0001,
	Pending = 0b_0010,
	Failure = 0b_0011,
	Handled = 0b_0111
	// 0111 bitmask for Handled means that if any of the three lower bits is set, the message
	// hs been handled.
}

public interface ISecondaryIndexProcessor {
	ValueTask<EventHandlingStatus> Index(ResolvedEvent resolvedEvent, CancellationToken token = default);
	ValueTask Commit(CancellationToken token = default);
}

public record struct SequenceRecord(long Id, long Sequence);
