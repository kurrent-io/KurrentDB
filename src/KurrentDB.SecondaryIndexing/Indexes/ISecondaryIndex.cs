// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondaryIndexing.Indexes;

public interface ISecondaryIndex: IDisposable {
	void Init();

	ulong? GetLastPosition();

	ISecondaryIndexProcessor Processor { get; }

	IReadOnlyList<IVirtualStreamReader> Readers { get; }
}

public interface ISecondaryIndexProcessor {
	void Index(ResolvedEvent resolvedEvent);

	void Commit();
}

public record struct SequenceRecord(long Id, long Sequence);
