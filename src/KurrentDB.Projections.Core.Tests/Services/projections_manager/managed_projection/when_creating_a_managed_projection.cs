// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.Messaging;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;
using core_projection_TestFixtureWithReadWriteDispatchers = KurrentDB.Projections.Core.Tests.Services.core_projection.TestFixtureWithReadWriteDispatchers;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager.managed_projection;

[TestFixture]
public class when_creating_a_managed_projection : core_projection_TestFixtureWithReadWriteDispatchers {
	private new ITimeProvider _timeProvider;

	private
		RequestResponseDispatcher<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>
		_getStateDispatcher;

	private
		RequestResponseDispatcher
		<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>
		_getResultDispatcher;

	[SetUp]
	public void setup() {
		_timeProvider = new FakeTimeProvider();
		_getStateDispatcher =
			new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetState, CoreProjectionStatusMessage.StateReport>(
					_bus,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_bus);
		_getResultDispatcher =
			new RequestResponseDispatcher
				<CoreProjectionManagementMessage.GetResult, CoreProjectionStatusMessage.ResultReport>(
					_bus,
					v => v.CorrelationId,
					v => v.CorrelationId,
					_bus);
	}


	[Test]
	public void empty_guid_throws_invalid_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new ManagedProjection(
				Guid.NewGuid(),
				Guid.Empty,
				1,
				"name",
				true,
				null,
				_streamDispatcher,
				_writeDispatcher,
				_readDispatcher,
				_bus,
				_timeProvider,
				_getStateDispatcher,
				_getResultDispatcher,
				_ioDispatcher,
				TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
		});
	}

	[Test]
	public void null_name_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			new ManagedProjection(
				Guid.NewGuid(),
				Guid.NewGuid(),
				1,
				null,
				true,
				null,
				_streamDispatcher,
				_writeDispatcher,
				_readDispatcher,
				_bus,
				_timeProvider,
				_getStateDispatcher,
				_getResultDispatcher,
				_ioDispatcher,
				TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
		});
	}

	[Test]
	public void empty_name_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			new ManagedProjection(
				Guid.NewGuid(),
				Guid.NewGuid(),
				1,
				"",
				true,
				null,
				_streamDispatcher,
				_writeDispatcher,
				_readDispatcher,
				_bus,
				_timeProvider,
				_getStateDispatcher,
				_getResultDispatcher,
				_ioDispatcher,
				TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault));
		});
	}
}
