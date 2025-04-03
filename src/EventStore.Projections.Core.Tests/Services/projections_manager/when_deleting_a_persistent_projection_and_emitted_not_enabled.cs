// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Tests.Services.projections_manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Core.Services;
using EventStore.Common.Utils;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using Messages;
using NUnit.Framework;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class WhenDeletingAPersistentProjectionAndEmittedNotEnabled<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;

	protected override void Given() {
		_projectionName = "test-projection";
		AllWritesSucceed();
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
			                                             _bus, ProjectionMode.Continuous, _projectionName,
			                                             ProjectionManagementMessage.RunAs.System, "JS", @"fromAll().when({$any:function(s,e){return s;}});",
			                                             enabled: true, checkpointsEnabled: true, emitEnabled: false, trackEmittedStreams: false);
		yield return
			new ProjectionManagementMessage.Command.Disable(
			                                                _bus, _projectionName, ProjectionManagementMessage.RunAs.System);
		yield return
			new ProjectionManagementMessage.Command.Delete(
			                                               _bus, _projectionName,
			                                               ProjectionManagementMessage.RunAs.System, true, true, true);
	}

	[Test, Category("v8")]
	public void a_projection_deleted_event_is_written() {
		var deletedStreamEvents = _consumer.HandledMessages.OfType<ClientMessage.DeleteStream>().ToList();

		Assert.AreEqual(deletedStreamEvents.Count, 1);

		Assert.AreEqual(deletedStreamEvents.First().EventStreamId,$"$projections-{_projectionName}-checkpoint");

		Assert.AreEqual(true, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Any(x => x.Events[0].EventType == ProjectionEventTypes.ProjectionDeleted && Helper.UTF8NoBom.GetString(x.Events[0].Data) == _projectionName));
	}
}
