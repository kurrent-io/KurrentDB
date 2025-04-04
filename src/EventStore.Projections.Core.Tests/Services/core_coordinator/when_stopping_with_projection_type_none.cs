using System;
using System.Collections.Generic;
using EventStore.Common.Options;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator {
	[TestFixture]
	public class when_stopping_with_projection_type_none {
		private FakePublisher[] queues;
		private FakePublisher publisher;
		private ProjectionCoreCoordinator _coordinator;
		private TimeoutScheduler[] timeoutScheduler = { };
		private FakeEnvelope envelope = new FakeEnvelope();

		[SetUp]
		public void Setup() {
			queues = new List<FakePublisher>() { new FakePublisher() }.ToArray();
			publisher = new FakePublisher();

			var instanceCorrelationId = Guid.NewGuid();
			_coordinator =
				new ProjectionCoreCoordinator(ProjectionType.None, timeoutScheduler, queues, publisher, envelope);

			// Start components
			_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));

			// start sub components
			_coordinator.Handle(
				new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
					instanceCorrelationId));

			//force stop
			_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));
		}

		[Test]
		public void should_publish_stop_reader_messages() {
			Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
		}

		[Test]
		public void should_not_publish_stop_core_messages() {
			Assert.AreEqual(0,
				queues[0].Messages.FindAll(x => x.GetType() == typeof(ProjectionCoreServiceMessage.StopCore)).Count);
		}
	}
}
