// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.SecondaryIndexing.Indices;

namespace KurrentDB.SecondaryIndexing.Tests.Indices;

public class FakeSecondaryIndex : ISecondaryIndex {
	public FakeSecondaryIndex(string streamName) {
		Committed = [];
		Processor = new FakeSecondaryIndexProcessor(Committed, Pending);
		Readers = [new FakeVirtualStreamReader(streamName, Committed.AsReadOnly())];
	}

	public IList<ResolvedEvent> Committed { get; }
	public IList<ResolvedEvent> Pending { get; } = new List<ResolvedEvent>();

	public ISecondaryIndexProcessor Processor { get; }
	public IReadOnlyList<IVirtualStreamReader> Readers { get; }
	public void Init() {}

	public ulong? GetLastPosition() =>
		Committed.Select(@event => (ulong?)@event.Event.LogPosition).FirstOrDefault();

	public ulong? GetLastSequence() =>
		Committed.Select(@event => (ulong?)@event.Event.EventNumber).FirstOrDefault();

	public void Dispose() { }
}
