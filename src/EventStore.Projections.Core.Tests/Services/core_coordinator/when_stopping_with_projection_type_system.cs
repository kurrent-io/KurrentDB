using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Options;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator {
	[TestFixture]
	public class when_stopping_with_projection_type_system {
		private FakePublisher[] queues;
		private FakePublisher publisher;
		private ProjectionCoreCoordinator _coordinator;
		private TimeoutScheduler[] timeoutScheduler = { };
		private FakeEnvelope envelope = new FakeEnvelope();

		private List<ProjectionCoreServiceMessage.StopCore> stopCoreMessages =
			new List<ProjectionCoreServiceMessage.StopCore>();

		[SetUp]
		public void Setup() {
			queues = new List<FakePublisher>() { new FakePublisher() }.ToArray();
			publisher = new FakePublisher();

			var instanceCorrelationId = Guid.NewGuid();
			_coordinator =
				new ProjectionCoreCoordinator(ProjectionType.System, timeoutScheduler, queues, publisher, envelope);

			// Start components
			_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));

			// Start all sub components
			_coordinator.Handle(
				new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
					instanceCorrelationId));
			_coordinator.Handle(
				new ProjectionCoreServiceMessage.SubComponentStarted(ProjectionCoreService.SubComponentName,
					instanceCorrelationId));

			// Stop components
			_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));

			// Publish SubComponent stopped messages for the projection core service
			stopCoreMessages = queues[0].Messages.OfType<ProjectionCoreServiceMessage.StopCore>().ToList();
			foreach (var msg in stopCoreMessages)
				_coordinator.Handle(
					new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
						msg.QueueId));
		}

		[Test]
		public void should_publish_stop_core_messages() {
			Assert.AreEqual(1, stopCoreMessages.Count);
		}

		[Test]
		public void should_publish_stop_reader_messages() {
			Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
		}
	}
}
