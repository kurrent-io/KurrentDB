// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.TestAdapters;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_posting_a_projection_with_oversized_metadata<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private readonly byte[] _oversizedMetadata = new byte[TFConsts.EffectiveMaxLogRecordSize];

	protected override void Given() {
		_projectionName = "test-projection";
		NoStream("$projections-test-projection");
		NoStream("$projections-test-projection-result");
		NoStream("$projections-test-projection-order");
		AllWritesToSucceed("$projections-test-projection-order");
		NoStream("$projections-test-projection-checkpoint");
		AllWritesSucceed();
	}

	protected override IEnumerable<WhenStep> When() {
		yield return new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid());
		yield return
			new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionMode.Continuous, _projectionName,
				ProjectionManagementMessage.RunAs.System, "JS", @"fromAll().when({$any:function(s,e){return s;}});",
				enabled: true, checkpointsEnabled: true, emitEnabled: true, trackEmittedStreams: true,
				metadata: _oversizedMetadata);
	}

	[Test, Category("v8")]
	public void the_operation_fails_as_record_too_large() {
		var failure = _consumer.HandledMessages.OfType<ProjectionManagementMessage.RecordTooLarge>().Single();
		StringAssert.Contains("exceeds the maximum", failure.Reason);
	}

	[Test, Category("v8")]
	public void no_definition_event_is_written() {
		var stream = ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName;
		Assert.IsEmpty(_consumer.HandledMessages.OfType<ClientMessage.WriteEvents>()
			.Where(x => x.EventStreamId == stream && x.Events[0].EventType == ProjectionEventTypes.ProjectionUpdated)
			.ToList());
	}

	[Test, Category("v8")]
	public void the_projection_is_faulted() {
		_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, _projectionName));
		Assert.AreEqual(
			ManagedProjectionState.Faulted,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Last().Projections.Single()
				.LeaderStatus);
	}
}
