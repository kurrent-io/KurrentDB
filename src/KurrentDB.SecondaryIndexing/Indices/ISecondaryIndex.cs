// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondaryIndexing.Indices;

public interface ISecondaryIndex: IDisposable {
	void Init();

	ulong? GetLastPosition();

	ulong? GetLastSequence();

	ISecondaryIndexProcessor Processor { get; }

	IReadOnlyList<IVirtualStreamReader> Readers { get; }
}

public interface ISecondaryIndexProcessor {
	ValueTask Index(ResolvedEvent resolvedEvent, CancellationToken token = default);
	ValueTask Commit(CancellationToken token = default);
}

public record struct SequenceRecord(long Id, long Sequence);
