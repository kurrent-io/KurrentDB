// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Tests;
using KurrentDB.Projections.Core.Messages;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionManagementMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_onetime_projection_has_been_posted<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private string _projectionQuery;

	protected override void Given() {
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		_projectionQuery = "fromAll(); on_any(function(){});log(1);";
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		yield return
			new Command.Post(
				_bus, RunAs.Anonymous, _projectionQuery,
				enabled: true);
		_projectionName = _consumer.HandledMessages.OfType<Updated>().Single().Name;
	}

	[Test, Category("v8")]
	public void it_has_been_posted() {
		Assert.IsFalse(string.IsNullOrEmpty(_projectionName));
	}

	[Test, Category("v8")]
	public void it_cab_be_listed() {
		_manager.Handle(new Command.GetStatistics(_bus, null, null));

		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<Statistics>().Count(v => v.Projections.Any(p => p.Name == _projectionName)));
	}

	[Test, Category("v8")]
	public void the_projection_status_can_be_retrieved() {
		_manager.Handle(new Command.GetStatistics(_bus, null, _projectionName));

		var actual = _consumer.HandledMessages.OfType<Statistics>().Single();
		Assert.AreEqual(1, actual.Projections.Length);
		Assert.AreEqual(_projectionName, actual.Projections.Single().Name);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(new Command.GetState(_bus, _projectionName, ""));
		_queue.Process();

		var actual = _consumer.HandledMessages.OfType<ProjectionState>().Single();
		Assert.AreEqual(_projectionName, actual.Name);
		Assert.AreEqual("", actual.State);
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(new Command.GetQuery(_bus, _projectionName, RunAs.Anonymous));

		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionQuery>().Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
		Assert.AreEqual(_projectionQuery, projectionQuery.Query);
	}
}
