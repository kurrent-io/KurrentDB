// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using EventStore.Core.Bus;
using EventStore.Core.Tests;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection.projection_checkpoint_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_projection_reader_reads_successfully<TLogFormat, TStreamId> : with_projection_checkpoint_reader<TLogFormat, TStreamId>,
	IHandle<CoreProjectionProcessingMessage.CheckpointLoaded> {
	private ManualResetEventSlim _mre = new ManualResetEventSlim();
	private CoreProjectionProcessingMessage.CheckpointLoaded _checkpointLoaded;

	public override void When() {
		_bus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(this);

		_reader.Initialize();
		_reader.BeginLoadState();
		if (!_mre.Wait(10000)) {
			Assert.Fail("Timed out waiting for checkpoint to load");
		}
	}

	public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) {
		_checkpointLoaded = message;
		_mre.Set();
	}

	[Test]
	public void should_load_checkpoint() {
		Assert.IsNotNull(_checkpointLoaded);
		Assert.AreEqual(_checkpointLoaded.ProjectionId, _projectionId);
	}
}
