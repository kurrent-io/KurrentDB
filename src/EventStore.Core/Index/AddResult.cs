// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Core.Index;

public class AddResult {
	public readonly IndexMap NewMap;
	public readonly bool CanMergeAny;

	public AddResult(IndexMap newMap, bool canMergeAny) {
		NewMap = newMap;
		CanMergeAny = canMergeAny;
	}
}
