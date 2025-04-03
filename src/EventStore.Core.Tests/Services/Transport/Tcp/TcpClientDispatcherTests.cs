// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Services.Transport.Tcp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Authentication;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using System.Text;
using EventStore.Client.Messages;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.LogV2;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Services;
using EventStore.Core.Tests.Authorization;
using EventStore.Core.Util;
using EventRecord = EventStore.Core.Data.EventRecord;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class TcpClientDispatcherTests {
	private readonly NoopEnvelope _envelope = new NoopEnvelope();

	private ClientTcpDispatcher _dispatcher;
	private TcpConnectionManager _connection;

	[OneTimeSetUp]
	public void Setup() {
		_dispatcher = new ClientTcpDispatcher(2000);

		var dummyConnection = new DummyTcpConnection();
		_connection = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(), new InternalAuthenticationProvider(
				InMemoryBus.CreateTest(), new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()),
				new StubPasswordHashAlgorithm(), 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { },
			Opts.ConnectionPendingSendBytesThresholdDefault, Opts.ConnectionQueueSizeThresholdDefault);
	}

	[Test]
	public void
		when_wrapping_read_stream_events_forward_and_stream_was_deleted_should_not_downgrade_last_event_number_for_v2_clients() {
		var msg = new ClientMessage.ReadStreamEventsForwardCompleted(Guid.NewGuid(), "test-stream", 0, 100,
			ReadStreamResult.StreamDeleted, new ResolvedEvent[0], new StreamMetadata(),
			true, "", -1, long.MaxValue, true, 1000);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadStreamEventsForwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadStreamEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");

		Assert.AreEqual(long.MaxValue, dto.LastEventNumber, "Last Event Number");
	}

	[Test]
	public void
		when_wrapping_read_stream_events_backward_and_stream_was_deleted_should_not_downgrade_last_event_number_for_v2_clients() {
		var msg = new ClientMessage.ReadStreamEventsBackwardCompleted(Guid.NewGuid(), "test-stream", 0, 100,
			ReadStreamResult.StreamDeleted, new ResolvedEvent[0], new StreamMetadata(),
			true, "", -1, long.MaxValue, true, 1000);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadStreamEventsBackwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadStreamEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");

		Assert.AreEqual(long.MaxValue, dto.LastEventNumber, "Last Event Number");
	}

	[Test]
	public void
		when_wrapping_read_all_events_forward_completed_with_deleted_event_should_not_downgrade_last_event_number_for_v2_clients() {
		var events = new ResolvedEvent[] {
			ResolvedEvent.ForUnresolvedEvent(CreateDeletedEventRecord(), 0),
		};
		var msg = new ClientMessage.ReadAllEventsForwardCompleted(Guid.NewGuid(), ReadAllResult.Success, "", events,
			new StreamMetadata(), true, 10, new TFPos(0, 0),
			new TFPos(200, 200), new TFPos(0, 0), 100);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadAllEventsForwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadAllEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(1, dto.Events.Count(), "Number of events");

		Assert.AreEqual(long.MaxValue, dto.Events[0].Event.EventNumber, "Event Number");
	}

	[Test]
	public void
		when_wrapping_read_all_events_forward_completed_with_link_to_deleted_event_should_not_downgrade_version_for_v2_clients() {
		var events = new ResolvedEvent[] {
			ResolvedEvent.ForResolvedLink(CreateLinkEventRecord(), CreateDeletedEventRecord(), 100)
		};
		var msg = new ClientMessage.ReadAllEventsForwardCompleted(Guid.NewGuid(), ReadAllResult.Success, "", events,
			new StreamMetadata(), true, 10, new TFPos(0, 0),
			new TFPos(200, 200), new TFPos(0, 0), 100);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadAllEventsForwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadAllEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(1, dto.Events.Count(), "Number of events");

		Assert.AreEqual(0, dto.Events[0].Event.EventNumber, "Event Number");
		Assert.AreEqual(long.MaxValue, dto.Events[0].Link.EventNumber, "Link Event Number");
	}

	[Test]
	public void
		when_wrapping_read_all_events_backward_completed_with_deleted_event_should_not_downgrade_version_for_v2_clients() {
		var events = new ResolvedEvent[] {
			ResolvedEvent.ForUnresolvedEvent(CreateDeletedEventRecord(), 0),
		};
		var msg = new ClientMessage.ReadAllEventsBackwardCompleted(Guid.NewGuid(), ReadAllResult.Success, "",
			events,
			new StreamMetadata(), true, 10, new TFPos(0, 0),
			new TFPos(200, 200), new TFPos(0, 0), 100);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadAllEventsBackwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadAllEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(1, dto.Events.Count(), "Number of events");

		Assert.AreEqual(long.MaxValue, dto.Events[0].Event.EventNumber, "Event Number");
	}

	[Test]
	public void
		when_wrapping_read_all_events_backward_completed_with_link_to_deleted_event_should_not_downgrade_version_for_v2_clients() {
		var events = new ResolvedEvent[] {
			ResolvedEvent.ForResolvedLink(CreateLinkEventRecord(), CreateDeletedEventRecord(), 100)
		};
		var msg = new ClientMessage.ReadAllEventsBackwardCompleted(Guid.NewGuid(), ReadAllResult.Success, "",
			events,
			new StreamMetadata(), true, 10, new TFPos(0, 0),
			new TFPos(200, 200), new TFPos(0, 0), 100);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ReadAllEventsBackwardCompleted, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ReadAllEventsCompleted>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(1, dto.Events.Count(), "Number of events");

		Assert.AreEqual(0, dto.Events[0].Event.EventNumber, "Event Number");
		Assert.AreEqual(long.MaxValue, dto.Events[0].Link.EventNumber, "Link Event Number");
	}

	[Test]
	public void
		when_wrapping_stream_event_appeared_with_deleted_event_should_not_downgrade_version_for_v2_clients() {
		var msg = new ClientMessage.StreamEventAppeared(Guid.NewGuid(),
			ResolvedEvent.ForUnresolvedEvent(CreateDeletedEventRecord(), 0));

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.StreamEventAppeared, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<StreamEventAppeared>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(long.MaxValue, dto.Event.Event.EventNumber, "Event Number");
	}

	[Test]
	public void
		when_wrapping_subscribe_to_stream_confirmation_when_stream_deleted_should_not_downgrade_version_for_v2_clients() {
		var msg = new ClientMessage.SubscriptionConfirmation(Guid.NewGuid(), 100, long.MaxValue);
		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.SubscriptionConfirmation, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<SubscriptionConfirmation>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(long.MaxValue, dto.LastEventNumber, "Last Event Number");
	}

	[Test]
	public void
		when_wrapping_subscribe_to_stream_confirmation_when_stream_deleted_should_not_downgrade_last_event_number_for_v2_clients() {
		var msg = new ClientMessage.SubscriptionConfirmation(Guid.NewGuid(), 100, long.MaxValue);
		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.SubscriptionConfirmation, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<SubscriptionConfirmation>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(long.MaxValue, dto.LastEventNumber, "Last Event Number");
	}

	[Test]
	public void
		when_wrapping_stream_event_appeared_with_link_to_deleted_event_should_not_downgrade_version_for_v2_clients() {
		var msg = new ClientMessage.StreamEventAppeared(Guid.NewGuid(),
			ResolvedEvent.ForResolvedLink(CreateLinkEventRecord(), CreateDeletedEventRecord(), 0));

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.StreamEventAppeared, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<StreamEventAppeared>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(0, dto.Event.Event.EventNumber, "Event Number");
		Assert.AreEqual(long.MaxValue, dto.Event.Link.EventNumber, "Link Event Number");
	}

	[Test]
	public void
		when_wrapping_persistent_subscription_confirmation_when_stream_deleted_should_not_downgrade_last_event_number_for_v2_clients() {
		var msg = new ClientMessage.PersistentSubscriptionConfirmation("subscription", Guid.NewGuid(), 100,
			long.MaxValue);
		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.PersistentSubscriptionConfirmation, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<PersistentSubscriptionConfirmation>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(long.MaxValue, dto.LastEventNumber, "Last event number");
	}

	[Test]
	public void
		when_wrapping_scavenge_started_response_should_return_result_and_scavengeId_for_v2_clients() {
		var scavengeId = Guid.NewGuid().ToString();
		var msg = new ClientMessage.ScavengeDatabaseStartedResponse(Guid.NewGuid(), scavengeId);

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ScavengeDatabaseResponse, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ScavengeDatabaseResponse>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(dto.Result, ScavengeDatabaseResponse.Types.ScavengeResult.Started);
		Assert.AreEqual(dto.ScavengeId, scavengeId);
	}

	[Test]
	public void
		when_wrapping_scavenge_inprogress_response_should_return_result_and_scavengeId_for_v2_clients() {
		var scavengeId = Guid.NewGuid().ToString();
		var msg = new ClientMessage.ScavengeDatabaseInProgressResponse(Guid.NewGuid(), scavengeId, reason:"In Progress");

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ScavengeDatabaseResponse, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ScavengeDatabaseResponse>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(dto.Result, ScavengeDatabaseResponse.Types.ScavengeResult.InProgress);
		Assert.AreEqual(dto.ScavengeId, scavengeId);
	}

	[Test]
	public void
		when_wrapping_scavenge_unauthorized_response_should_return_result_and_scavengeId_for_v2_clients() {
		var scavengeId = Guid.NewGuid().ToString();
		var msg = new ClientMessage.ScavengeDatabaseUnauthorizedResponse(Guid.NewGuid(), scavengeId,"Unauthorized" );

		var package = _dispatcher.WrapMessage(msg, (byte)ClientVersion.V2);
		Assert.IsNotNull(package, "Package is null");
		Assert.AreEqual(TcpCommand.ScavengeDatabaseResponse, package.Value.Command, "TcpCommand");

		var dto = package.Value.Data.Deserialize<ScavengeDatabaseResponse>();
		Assert.IsNotNull(dto, "DTO is null");
		Assert.AreEqual(dto.Result, ScavengeDatabaseResponse.Types.ScavengeResult.Unauthorized);
		Assert.AreEqual(dto.ScavengeId, scavengeId);
	}

	private EventRecord CreateDeletedEventRecord() {
		return new EventRecord(long.MaxValue,
			LogRecord.DeleteTombstone(new LogV2RecordFactory(), 0, Guid.NewGuid(), Guid.NewGuid(),
				"test-stream", "test-type", long.MaxValue), "test-stream", SystemEventTypes.StreamDeleted);
	}

	private EventRecord CreateLinkEventRecord() {
		return new EventRecord(0, LogRecord.Prepare(new LogV2RecordFactory(), 100, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
			"link-stream", -1, PrepareFlags.SingleWrite | PrepareFlags.Data, SystemEventTypes.LinkTo,
			Encoding.UTF8.GetBytes(string.Format("{0}@test-stream", long.MaxValue)), new byte[0]), "link-stream", SystemEventTypes.LinkTo);
	}
}
