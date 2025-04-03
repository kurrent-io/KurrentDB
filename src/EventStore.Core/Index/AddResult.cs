// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;

namespace EventStore.Core.Index;

public class AddResult {
	public readonly IndexMap NewMap;
	public readonly bool CanMergeAny;

	public AddResult(IndexMap newMap, bool canMergeAny) {
		NewMap = newMap;
		CanMergeAny = canMergeAny;
	}
}
