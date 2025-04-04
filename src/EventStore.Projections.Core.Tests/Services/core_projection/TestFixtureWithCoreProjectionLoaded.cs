// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithCoreProjectionLoaded<TLogFormat, TStreamId> : TestFixtureWithCoreProjection<TLogFormat, TStreamId> {
	protected override void PreWhen() {
		_coreProjection.LoadStopped();
	}
}
