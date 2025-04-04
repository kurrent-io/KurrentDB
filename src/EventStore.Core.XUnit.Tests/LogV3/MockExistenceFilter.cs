// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.XUnit.Tests.LogV3;

public class MockExistenceFilter : INameExistenceFilter {
	public HashSet<string> Streams { get; } = new();

	public long CurrentCheckpoint { get; set; } = -1;

	public void Add(string name) {
		Streams.Add(name);
	}

	public void Add(ulong hash) {
		throw new System.NotImplementedException();
	}

	public void Dispose() {
	}

	public ValueTask Initialize(INameExistenceFilterInitializer source, long truncateToPosition, CancellationToken token)
		=> source.Initialize(this, truncateToPosition, token);

	public void TruncateTo(long checkpoint) {
		CurrentCheckpoint = checkpoint;
	}

	public void Verify(double corruptionThreshold) { }

	public bool MightContain(string item) {
		return Streams.Contains(item);
	}
}
