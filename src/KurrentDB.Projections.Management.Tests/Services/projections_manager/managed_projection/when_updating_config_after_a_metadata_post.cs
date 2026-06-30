// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.managed_projection;

// A metadata-bearing create must not leak its blob onto a later, non-metadata config write through the shared
// CreatePersistedStateEvent chokepoint.
[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_updating_config_after_a_metadata_post<TLogFormat, TStreamId> : projection_config_test_base<TLogFormat, TStreamId> {
	private ManagedProjection _mp;
	private readonly Guid _projectionId = Guid.NewGuid();
	private readonly byte[] _metadata = Helper.UTF8NoBom.GetBytes("{\"deploy\":\"abc123\"}");

	private ManagedProjection.PersistedState _persistedState => new ManagedProjection.PersistedState {
		Enabled = false,
		HandlerType = "JS",
		Query = "fromAll().when({});",
		Mode = ProjectionMode.Continuous,
		CheckpointsDisabled = false,
		Epoch = -1,
		Version = -1,
		RunAs = SerializedRunAs.SerializePrincipal(ProjectionManagementMessage.RunAs.Anonymous),
		EmitEnabled = false,
		TrackEmittedStreams = true,
		CheckpointAfterMs = 1,
		CheckpointHandledThreshold = 2,
		CheckpointUnhandledBytesThreshold = 3,
		PendingEventsThreshold = 4,
		MaxWriteBatchLength = 5,
		MaxAllowedWritesInFlight = 6,
		ProjectionExecutionTimeout = 11
	};

	public when_updating_config_after_a_metadata_post() {
		AllWritesQueueUp();
	}

	protected override void Given() {
		_timeProvider = new FakeTimeProvider();
		_mp = CreateManagedProjection();

		_mp.InitializeNew(_persistedState, null, _metadata);
		_mp.Handle(new CoreProjectionStatusMessage.Prepared(_projectionId, new ProjectionSourceDefinition()));
		OneWriteCompletes();
		_mp.Handle(new CoreProjectionStatusMessage.Stopped(_projectionId, ProjectionName, false));

		_mp.Handle(CreateConfig());
		OneWriteCompletes();
	}

	[Test]
	public void the_create_write_carried_the_metadata() {
		CollectionAssert.AreEqual(_metadata, _streams[ProjectionStreamId].First().Metadata.ToArray());
	}

	[Test]
	public void the_config_write_is_metadata_less() {
		Assert.AreEqual(0, _streams[ProjectionStreamId].Last().Metadata.Length);
	}
}
