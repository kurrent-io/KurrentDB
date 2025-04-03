// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Index;

public interface IIndexScavengerLog {
	void IndexTableScavenged(int level, int index, TimeSpan elapsed, long entriesDeleted, long entriesKept,
		long spaceSaved);

	void IndexTableNotScavenged(int level, int index, TimeSpan elapsed, long entriesKept, string errorMessage);
}
