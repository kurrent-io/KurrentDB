// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.POC.IO.Core;

namespace KurrentDB.AutoScavenge.Tests;

public class DummyOperationClient : IOperationsClient {
	public void Resign() {
	}

	public Task<Guid?> StartScavengeAsync(CancellationToken token) => throw new NotSupportedException();

	public Task<LastScavenge> GetLastScavengeAsync(CancellationToken token) => throw new NotSupportedException();

	public Task<bool> StopScavengeAsync(Guid? scavengeId, CancellationToken token) => throw new NotSupportedException();
}
