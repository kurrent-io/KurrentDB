// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using Kurrent.Quack;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondaryIndexing.Indices.Stream;

internal class StreamIndex(DuckDBAdvancedConnection connection) : ISecondaryIndex {
	private readonly StreamIndexProcessor _processor = new(connection);

	public void Init() {
	}

	public ulong? GetLastPosition() =>
		(ulong)_processor.LastCommittedPosition;

	public ulong? GetLastSequence() => (ulong)_processor.Seq;

	public ISecondaryIndexProcessor Processor => _processor;
	public IReadOnlyList<IVirtualStreamReader> Readers { get; } = [];

	public long LastIndexed => _processor.LastIndexed;

	public void Dispose() { }
}
