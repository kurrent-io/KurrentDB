// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.LogAbstraction;

namespace EventStore.Core.XUnit.Tests.LogAbstraction;

public class MockExistenceFilterInitializer : INameExistenceFilterInitializer {
	private readonly string[] _names;

	public MockExistenceFilterInitializer(params string[] names) {
		_names = names;
	}

	public ValueTask Initialize(INameExistenceFilter filter, long truncateToPosition, CancellationToken token) {
		int checkpoint = 0;
		foreach (var name in _names) {
			filter.Add(name);
			filter.CurrentCheckpoint = checkpoint++;
		}

		return ValueTask.CompletedTask;
	}
}
