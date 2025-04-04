// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Helpers;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Util;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture]
public class when_creating_a_projection {
	[SetUp]
	public void Setup() {
		var fakePublisher = new FakePublisher();
		_ioDispatcher = new IODispatcher(fakePublisher, fakePublisher, true);

		_subscriptionDispatcher = new ReaderSubscriptionDispatcher(new FakePublisher());
	}

	private readonly ProjectionConfig _defaultProjectionConfig = new ProjectionConfig(
		null, 5, 10, 1000, 250, true, true, true, true, true, 10000, 1, null);

	private IODispatcher _ioDispatcher;


	private ReaderSubscriptionDispatcher _subscriptionDispatcher;

	[Test]
	public void
		a_checkpoint_threshold_less_tan_checkpoint_handled_threshold_throws_argument_out_of_range_exception() {
		Assert.Throws<ArgumentException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			var projectionConfig = new ProjectionConfig(null, 10, 5, 1000, 250, true, true, false, false,
				true, 10000, 1, null);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				projectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_negative_checkpoint_handled_interval_throws_argument_out_of_range_exception() {
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			var projectionConfig = new ProjectionConfig(null, -1, 10, 1000, 250, true, true, false, false,
				true, 10000, 1, null);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				projectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_io_dispatcher__throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				null,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_name_throws_argument_null_excveption() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				null,
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_publisher_throws_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				null,
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_input_queue_throws_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				null,
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_run_as_does_not_throw_exception() {
		IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
		var version = new ProjectionVersion(1, 0, 0);
		new ContinuousProjectionProcessingStrategy(
			"projection",
			version,
			projectionStateHandler,
			_defaultProjectionConfig,
			projectionStateHandler.GetSourceDefinition(),
			null,
			_subscriptionDispatcher,
			true, Opts.MaxProjectionStateSizeDefault).Create(
			Guid.NewGuid(),
			new FakePublisher(),
			Guid.NewGuid(),
			null,
			new FakePublisher(),
			_ioDispatcher,
			_subscriptionDispatcher,
			new RealTimeProvider());
	}

	[Test]
	public void a_null_subscription_dispatcher__throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				null,
				new RealTimeProvider());
		});
	}

	[Test]
	public void a_null_time_provider__throws_argument_null_exception() {
		Assert.Throws<ArgumentNullException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				null);
		});
	}

	[Test]
	public void a_zero_checkpoint_handled_threshold_throws_argument_out_of_range_exception() {
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			var projectionConfig = new ProjectionConfig(null, 0, 10, 1000, 250, true, true, false, false,
				true, 10000, 1, null);
			new ContinuousProjectionProcessingStrategy(
				"projection",
				version,
				projectionStateHandler,
				projectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}

	[Test]
	public void an_empty_name_throws_argument_exception() {
		Assert.Throws<ArgumentException>(() => {
			IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
			var version = new ProjectionVersion(1, 0, 0);
			new ContinuousProjectionProcessingStrategy(
				"",
				version,
				projectionStateHandler,
				_defaultProjectionConfig,
				projectionStateHandler.GetSourceDefinition(),
				null,
				_subscriptionDispatcher,
				true, Opts.MaxProjectionStateSizeDefault).Create(
				Guid.NewGuid(),
				new FakePublisher(),
				Guid.NewGuid(),
				SystemAccounts.System,
				new FakePublisher(),
				_ioDispatcher,
				_subscriptionDispatcher,
				new RealTimeProvider());
		});
	}
}
