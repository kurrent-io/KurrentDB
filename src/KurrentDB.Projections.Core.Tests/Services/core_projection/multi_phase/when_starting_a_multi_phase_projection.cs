// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Tests;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.multi_phase;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
class when_starting_a_multi_phase_projection<TLogFormat, TStreamId> : specification_with_multi_phase_core_projection<TLogFormat, TStreamId> {
	protected override void When() {
		_coreProjection.Start();
	}

	[Test]
	public void it_starts() {
	}
}
