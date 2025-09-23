// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Tests.Services.projections_manager;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

/**
 * tests whether race condition exists or not; currently deleting a projection involves following operations
 * 1. Deleting projection "sub-streams" (checkpoint, emitted, etc. streams)
 * 2. Writing new projection persisted state to $projection-<projection_name> stream
 *
 * steps 1 and 2 are independent and gives rise to race condition :
 * * if step 2 completes after step 1, multiple ProjectionManagement.Internal.Deleted events will be published
 * * in addition, if step 2 completes shortly after step 1, WrongExpectedVersion will be encountered
 */
public static class when_writing_projection_persisted_state_races_with_stream_deletion {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_projection_persisted_state_write_races_with_projections_substream_deletion<TLogFormat, TStreamId>
		: TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
		private const string ProjectionName = "my-projection";

		protected override void Given() {
			base.Given();
			NoOtherStreams();
			AllWritesSucceed();
		}

		protected override IEnumerable<WhenStep> When() {
			yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
			yield return
				new ProjectionManagementMessage.Command.Post(
					_bus, ProjectionMode.Continuous, ProjectionName,
					ProjectionManagementMessage.RunAs.System, "native:" + typeof(FakeProjection).AssemblyQualifiedName,
					"", enabled: true, checkpointsEnabled: true,
					emitEnabled: false, trackEmittedStreams: false);
			yield return new ProjectionManagementMessage.Command.Disable(_bus, ProjectionName, ProjectionManagementMessage.RunAs.System);
			yield return new ProjectionManagementMessage.Command.Delete(new NoopEnvelope(), ProjectionName, ProjectionManagementMessage.RunAs.System, true,
				false, false);
		}

		[Test]
		public void should_publish_single_projection_deleted_event() {
			Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Internal.Deleted>().Count());
		}
	}
}
