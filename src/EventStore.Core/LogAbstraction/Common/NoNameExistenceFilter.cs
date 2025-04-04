// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.LogAbstraction.Common;

public class NoNameExistenceFilter : INameExistenceFilter {
	public ValueTask Initialize(INameExistenceFilterInitializer source, long truncateToPosition,
		CancellationToken token)
		=> token.IsCancellationRequested ? ValueTask.FromCanceled(token) : ValueTask.CompletedTask;

	public void TruncateTo(long checkpoint) { }
	public void Verify(double corruptionThreshold) { }
	public long CurrentCheckpoint { get; set; } = -1;

	public void Add(string name) { }
	public void Add(ulong hash) { }
	public bool MightContain(string name) => true;
	public void Dispose() { }
}
