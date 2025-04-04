// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services.projections_system;

public abstract class with_projection_config<TLogFormat, TStreamId> : with_projections_subsystem<TLogFormat, TStreamId> {
	protected string _projectionName;
	protected string _projectionSource;
	protected bool _checkpointsEnabled;
	protected bool _trackEmittedStreams;
	protected bool _emitEnabled;

	protected override void Given() {
		base.Given();

		_projectionName = "test-projection";
		_projectionSource = @"";
		_checkpointsEnabled = true;
		_trackEmittedStreams = true;
		_emitEnabled = true;

		NoStream(ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName + "-checkpoint");
		NoStream(ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName + "-order");
		NoStream(ProjectionNamesBuilder.ProjectionsStreamPrefix + _projectionName + "-emittedstreams");
		AllWritesSucceed();
	}
}
