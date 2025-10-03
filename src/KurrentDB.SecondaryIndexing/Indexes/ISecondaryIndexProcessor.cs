// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Diagnostics;

namespace KurrentDB.SecondaryIndexing.Indexes;

public interface ISecondaryIndexProcessor : IDisposable {
	void Commit();

	void Index(ResolvedEvent evt);

	TFPos GetLastPosition();

	SecondaryIndexProgressTracker Tracker { get; }
}
