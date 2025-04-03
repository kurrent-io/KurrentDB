// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Processing;
using System.Collections.Generic;
using System.Collections;
using EventStore.Common.Utils;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Messages;
using LogV3StreamId = System.UInt32;

namespace EventStore.Projections.Core.Tests.Services.projections_manager.when_reading_registered_projections;

[TestFixture, TestFixtureSource(typeof(SystemProjectionNames))]
public class with_no_stream_and_intialize_system_projections<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _systemProjectionName;

	public with_no_stream_and_intialize_system_projections(string projectionName) {
		_systemProjectionName = projectionName;
	}

	protected override void Given() {
		AllWritesSucceed();
		NoStream(ProjectionNamesBuilder.ProjectionsRegistrationStream);
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
	}

	protected override bool GivenInitializeSystemProjections() {
		return true;
	}

	[Test]
	public void it_should_write_the_projections_initialized_event() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count(x =>
			x.EventStreamId == ProjectionNamesBuilder.ProjectionsRegistrationStream &&
			x.Events[0].EventType == ProjectionEventTypes.ProjectionsInitialized));
	}

	[Test]
	public void it_should_write_the_system_projection_created_event() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.WriteEvents>().Count(x =>
			x.EventStreamId == ProjectionNamesBuilder.ProjectionsRegistrationStream &&
			x.Events.All(e => e.EventType == ProjectionEventTypes.ProjectionCreated) &&
			x.Events.Any(e => Helper.UTF8NoBom.GetString(e.Data) == _systemProjectionName)));
	}
}

public class SystemProjectionNames : IEnumerable {
	public IEnumerator GetEnumerator() {
		foreach (var projection in typeof(ProjectionNamesBuilder.StandardProjections).GetFields(
				System.Reflection.BindingFlags.Public |
				System.Reflection.BindingFlags.Static |
				System.Reflection.BindingFlags.FlattenHierarchy)
			.Where(x => x.IsLiteral && !x.IsInitOnly)
			.Select(x => x.GetRawConstantValue())) {
			yield return new[] { typeof(LogFormat.V2), typeof(string), projection };
			yield return new[] { typeof(LogFormat.V3), typeof(LogV3StreamId), projection };
		}
	}
}
