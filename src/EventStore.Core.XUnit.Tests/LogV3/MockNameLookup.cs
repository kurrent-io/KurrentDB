// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.LogAbstraction;
using StreamId = System.UInt32;

namespace EventStore.Core.XUnit.Tests.LogV3;

class MockNameLookup : INameLookup<StreamId> {
	private readonly Dictionary<StreamId, string> _dict;

	public MockNameLookup(Dictionary<StreamId, string> dict) {
		_dict = dict;
	}

	public ValueTask<Optional<StreamId>> TryGetLastValue(CancellationToken token) {
		return new(_dict.Count > 0
			? _dict.Keys.Max()
			: Optional.None<StreamId>());
	}

	public ValueTask<string> LookupName(StreamId key, CancellationToken token) {
		return new(_dict.TryGetValue(key, out var name)
			? name
			: null);
	}
}
