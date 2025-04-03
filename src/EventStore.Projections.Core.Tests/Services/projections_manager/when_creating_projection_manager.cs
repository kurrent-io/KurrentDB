// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.Util;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;
using EventStore.Core.Helpers;
using EventStore.Projections.Core.Metrics;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture]
public class when_creating_projection_manager {
	private ITimeProvider _timeProvider;
	private Dictionary<Guid, IPublisher> _queues;
	private IODispatcher _ioDispatcher;

	[SetUp]
	public void setup() {
		_timeProvider = new FakeTimeProvider();
		_queues = new Dictionary<Guid, IPublisher> {{Guid.NewGuid(), new FakePublisher()}};
		var fakePublisher = new FakePublisher();
		new ProjectionCoreCoordinator(
			ProjectionType.All,
			_queues.Values.ToArray(),
			fakePublisher);
		_ioDispatcher = new IODispatcher(fakePublisher, fakePublisher, true);
	}

	[Test]
	public void it_can_be_created() {
		using (
			new ProjectionManager(
				new FakePublisher(),
				new FakePublisher(),
				_queues,
				_timeProvider,
				ProjectionType.All,
				_ioDispatcher,
				TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
				IProjectionTracker.NoOp)) {
		}
	}

	[Test]
	public void null_main_queue_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			using (
				new ProjectionManager(
					null,
					new FakePublisher(),
					_queues,
					_timeProvider,
					ProjectionType.All,
					_ioDispatcher,
					TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
					IProjectionTracker.NoOp)) {
			}
		});
	}

	[Test]
	public void null_publisher_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			using (
				new ProjectionManager(
					new FakePublisher(),
					null,
					_queues,
					_timeProvider,
					ProjectionType.All,
					_ioDispatcher,
					TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
					IProjectionTracker.NoOp)) {
			}
		});
	}

	[Test]
	public void null_queues_throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			using (
				new ProjectionManager(
					new FakePublisher(),
					new FakePublisher(),
					null,
					_timeProvider,
					ProjectionType.All,
					_ioDispatcher,
					TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
					IProjectionTracker.NoOp)) {
			}
		});
	}

	[Test]
	public void empty_queues_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			using (
				new ProjectionManager(
					new FakePublisher(),
					new FakePublisher(),
					new Dictionary<Guid, IPublisher>(),
					_timeProvider,
					ProjectionType.All,
					_ioDispatcher,
					TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
					IProjectionTracker.NoOp)) {
			}
		});
	}
}
