// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager.multi_stream;

public abstract class TestFixtureWithMultiStreamCheckpointManager<TLogFormat, TStreamId> : TestFixtureWithCoreProjectionCheckpointManager<TLogFormat, TStreamId> {
	protected new string[] _streams;

	protected override void Given() {
		base.Given();
		_projectionVersion = new ProjectionVersion(1, 0, 0);
		_streams = ["a", "b", "c"];
	}

	protected override DefaultCheckpointManager GivenCheckpointManager() {
		return new MultiStreamMultiOutputCheckpointManager(
			_bus, _projectionCorrelationId, _projectionVersion, null, _ioDispatcher, _config, _projectionName,
			new MultiStreamPositionTagger(0, _streams), _namingBuilder, _checkpointsEnabled,
			_checkpointWriter, Opts.MaxProjectionStateSizeDefault);
	}
}
