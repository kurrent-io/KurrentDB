// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_posting_an_onetime_projection<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected override void Given() {
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		yield return
			(new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionManagementMessage.RunAs.Anonymous,
				@"fromAll().when({$any:function(s,e){return s;}});", enabled: true));
	}

	[Test, Category("v8")]
	public void projection_updated_is_published() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Updated>().Count());
	}
}
