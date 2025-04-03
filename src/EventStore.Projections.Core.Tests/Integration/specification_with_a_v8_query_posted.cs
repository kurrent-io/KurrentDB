// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using DotNext;
using EventStore.Core.Bus;
using EventStore.Core.Messaging;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.projections_manager;

namespace EventStore.Projections.Core.Tests.Integration;

public abstract class specification_with_a_v8_query_posted<TLogFormat, TStreamId> : TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> {
	protected string _projectionName;
	protected string _projectionSource;
	protected ProjectionMode _projectionMode;
	protected bool _checkpointsEnabled;
	protected bool _trackEmittedStreams;
	protected bool _emitEnabled;
	protected bool _startSystemProjections;

	protected override void Given() {
		base.Given();
		AllWritesSucceed();
		NoOtherStreams();
		GivenEvents();
		EnableReadAll();
		_projectionName = "query";
		_projectionSource = GivenQuery();
		_projectionMode = ProjectionMode.Transient;
		_checkpointsEnabled = false;
		_trackEmittedStreams = false;
		_emitEnabled = false;
		_startSystemProjections = GivenStartSystemProjections();
	}

	protected override Tuple<SynchronousScheduler, IPublisher, SynchronousScheduler, Guid>[] GivenProcessingQueues() {
		SynchronousScheduler[] buses = [new("1"), new("2")];
		SynchronousScheduler[] outBuses = [new("o1"), new("o2")];
		_otherQueues = [new (buses[0], _timeProvider), new(buses[1], _timeProvider)];
		return [
			Tuple.Create(
				buses[0],
				_otherQueues[0].As<IPublisher>(),
				outBuses[0],
				Guid.NewGuid()),
			Tuple.Create(
				buses[1],
				_otherQueues[1].As<IPublisher>(),
				outBuses[1],
				Guid.NewGuid())
		];
	}

	protected abstract void GivenEvents();

	protected abstract string GivenQuery();

	protected virtual bool GivenStartSystemProjections() {
		return false;
	}

	protected Message CreateQueryMessage(string name, string source) {
		return new ProjectionManagementMessage.Command.Post(
			_bus, ProjectionMode.Transient, name,
			ProjectionManagementMessage.RunAs.System, "JS", source, enabled: true, checkpointsEnabled: false,
			trackEmittedStreams: false,
			emitEnabled: false);
	}

	protected Message CreateNewProjectionMessage(string name, string source) {
		return new ProjectionManagementMessage.Command.Post(
			_bus, ProjectionMode.Continuous, name, ProjectionManagementMessage.RunAs.System,
			"JS", source, enabled: true, checkpointsEnabled: true, trackEmittedStreams: true, emitEnabled: true);
	}

	protected override IEnumerable<WhenStep> When() {
		yield return (new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		if (_startSystemProjections) {
			yield return
				new ProjectionManagementMessage.Command.Enable(
					Envelope, ProjectionNamesBuilder.StandardProjections.StreamsStandardProjection,
					ProjectionManagementMessage.RunAs.System);
			yield return
				new ProjectionManagementMessage.Command.Enable(
					Envelope, ProjectionNamesBuilder.StandardProjections.StreamByCategoryStandardProjection,
					ProjectionManagementMessage.RunAs.System);
			yield return
				new ProjectionManagementMessage.Command.Enable(
					Envelope, ProjectionNamesBuilder.StandardProjections.EventByCategoryStandardProjection,
					ProjectionManagementMessage.RunAs.System);
			yield return
				new ProjectionManagementMessage.Command.Enable(
					Envelope, ProjectionNamesBuilder.StandardProjections.EventByTypeStandardProjection,
					ProjectionManagementMessage.RunAs.System);
		}

		var otherProjections = GivenOtherProjections();
		var index = 0;
		foreach (var source in otherProjections) {
			yield return
				(new ProjectionManagementMessage.Command.Post(
					_bus, ProjectionMode.Continuous, "other_" + index,
					ProjectionManagementMessage.RunAs.System, "JS", source, enabled: true, checkpointsEnabled: true,
					trackEmittedStreams: true,
					emitEnabled: true));
			index++;
		}

		if (!string.IsNullOrEmpty(_projectionSource)) {
			yield return
				(new ProjectionManagementMessage.Command.Post(
					_bus, _projectionMode, _projectionName,
					ProjectionManagementMessage.RunAs.System, "JS", _projectionSource, enabled: true,
					checkpointsEnabled: _checkpointsEnabled, emitEnabled: _emitEnabled,
					trackEmittedStreams: _trackEmittedStreams));
		}
	}

	protected virtual IEnumerable<string> GivenOtherProjections() {
		return new string[0];
	}
}
