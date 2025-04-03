// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Messaging;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_the_onetime_projection_has_been_posted<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	private string _projectionName;
	private string _projectionQuery;

	protected override void Given() {
		NoOtherStreams();
	}

	protected override IEnumerable<WhenStep> When() {
		_projectionQuery = @"fromAll(); on_any(function(){});log(1);";
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		yield return
			(new ProjectionManagementMessage.Command.Post(
				_bus, ProjectionManagementMessage.RunAs.Anonymous, _projectionQuery,
				enabled: true));
		_projectionName = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Updated>().Single().Name;
	}

	[Test, Category("v8")]
	public void it_has_been_posted() {
		Assert.IsFalse(string.IsNullOrEmpty(_projectionName));
	}

	[Test, Category("v8")]
	public void it_cab_be_listed() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, null, false));

		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count(
				v => v.Projections.Any(p => p.Name == _projectionName)));
	}

	[Test, Category("v8")]
	public void the_projection_status_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(
				_bus, null, _projectionName, false));

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Count());
		Assert.AreEqual(
			1,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Length);
		Assert.AreEqual(
			_projectionName,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().Single().Projections.Single()
				.Name);
	}

	[Test, Category("v8")]
	public void the_projection_state_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetState(_bus, _projectionName, ""));
		_queue.Process();

		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Count());
		Assert.AreEqual(
			_projectionName,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().Name);
		Assert.AreEqual(
			"", _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionState>().Single().State);
	}

	[Test, Category("v8")]
	public void the_projection_source_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetQuery(
				_bus, _projectionName, ProjectionManagementMessage.RunAs.Anonymous));
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Count());
		var projectionQuery = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>()
			.Single();
		Assert.AreEqual(_projectionName, projectionQuery.Name);
		Assert.AreEqual(_projectionQuery, projectionQuery.Query);
	}
}
