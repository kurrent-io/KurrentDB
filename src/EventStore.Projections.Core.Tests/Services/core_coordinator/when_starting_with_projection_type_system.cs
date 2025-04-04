using System;
using System.Collections.Generic;
using EventStore.Common.Options;
using EventStore.Core.Tests.Fakes;
using EventStore.Core.Tests.Services.Replication;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator {
	[TestFixture]
	public class when_starting_with_projection_type_system {
		private FakePublisher[] queues;
		private FakePublisher publisher;
		private ProjectionCoreCoordinator _coordinator;
		private TimeoutScheduler[] timeoutScheduler = { };
		private FakeEnvelope envelope = new FakeEnvelope();

		[SetUp]
		public void Setup() {
			queues = new List<FakePublisher>() { new FakePublisher() }.ToArray();
			publisher = new FakePublisher();

			_coordinator =
				new ProjectionCoreCoordinator(ProjectionType.System, timeoutScheduler, queues, publisher, envelope);
			_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
		}

		[Test]
		public void should_publish_start_reader_messages() {
			Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
		}

		[Test]
		public void should_publish_start_core_messages() {
			Assert.AreEqual(1,
				queues[0].Messages.FindAll(x => x.GetType() == typeof(ProjectionCoreServiceMessage.StartCore)).Count);
		}
	}
}
