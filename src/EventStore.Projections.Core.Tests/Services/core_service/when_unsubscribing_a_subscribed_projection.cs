// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Subscriptions;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_service;

[TestFixture]
public class when_unsubscribing_a_subscribed_projection : TestFixtureWithProjectionCoreService {
	private TestCoreProjection _committedeventHandler;
	private Guid _projectionCorrelationId;

	//private TestCoreProjection _committedeventHandler2;
	private Guid _projectionCorrelationId2;

	[SetUp]
	public new void Setup() {
		_committedeventHandler = new TestCoreProjection();
		//_committedeventHandler2 = new TestCoreProjection();
		_projectionCorrelationId = Guid.NewGuid();
		_projectionCorrelationId2 = Guid.NewGuid();
		_readerService.Handle(
			new ReaderSubscriptionManagement.Subscribe(
				_projectionCorrelationId, CheckpointTag.FromPosition(0, 0, 0), CreateReaderStrategy(),
				new ReaderSubscriptionOptions(1000, 2000, 10000, false, stopAfterNEvents: null, enableContentTypeValidation: true)));
		_readerService.Handle(
			new ReaderSubscriptionManagement.Subscribe(
				_projectionCorrelationId2, CheckpointTag.FromPosition(0, 0, 0), CreateReaderStrategy(),
				new ReaderSubscriptionOptions(1000, 2000, 10000, false, stopAfterNEvents: null, enableContentTypeValidation: true)));
		// when
		_readerService.Handle(new ReaderSubscriptionManagement.Unsubscribe(_projectionCorrelationId));
	}

	[Test]
	public void committed_events_are_no_longer_distributed_to_the_projection() {
		_readerService.Handle(
			new ReaderSubscriptionMessage.CommittedEventDistributed(_projectionCorrelationId, CreateEvent()));
		Assert.AreEqual(0, _committedeventHandler.HandledMessages.Count);
	}

	[Test]
	public void the_projection_cannot_be_resumed() {
		Assert.Throws<InvalidOperationException>(() => {
			_readerService.Handle(new ReaderSubscriptionManagement.Resume(_projectionCorrelationId));
			_readerService.Handle(
				new ReaderSubscriptionMessage.CommittedEventDistributed(_projectionCorrelationId, CreateEvent()));
			Assert.AreEqual(0, _committedeventHandler.HandledMessages.Count);
		});
	}
}
