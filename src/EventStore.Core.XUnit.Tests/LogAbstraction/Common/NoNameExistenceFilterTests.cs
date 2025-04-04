// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.LogAbstraction;
using EventStore.Core.LogAbstraction.Common;

namespace EventStore.Core.XUnit.Tests.LogAbstraction.Common;

public class NoNameExistenceFilterTests : INameExistenceFilterTests {
	protected override INameExistenceFilter Sut { get; set; } =
		new NoNameExistenceFilter();
}
