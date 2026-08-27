// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using MemoryContracts = Kurrent.Kontext.Contracts.Memory;

namespace Kurrent.Kontext.Tests.Infrastructure.Datasets;

/// <summary>
/// Streams memory events distilled from a corpus so tests and demos can seed the memory stream
/// with realistic data. Two contracts every implementation honors:
/// events arrive in source-chronological order — a memory that supersedes another is always
/// yielded after the memory it supersedes — and enumeration is lazy, O(one instance) in memory
/// regardless of corpus size. Supersession travels inside <c>Memory.supersedes</c>, so retained
/// events are the only vocabulary a seeding source needs.
/// </summary>
public interface IKontextTestDataSource {
    IAsyncEnumerable<MemoryContracts.MemoriesRetained> ReadEvents(CancellationToken ct = default);
}
