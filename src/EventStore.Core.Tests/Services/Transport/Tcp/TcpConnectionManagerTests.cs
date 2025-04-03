// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Services.Transport.Tcp;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using EventStore.Transport.Tcp;
using System.Net.Sockets;
using EventStore.Core.Messaging;
using EventStore.Core.Tests.Authentication;
using EventStore.Core.TransactionLog.LogRecords;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using System.Threading;
using EventStore.Client.Messages;
using EventStore.Core.Authentication.InternalAuthentication;
using EventStore.Core.Services;
using EventStore.Core.Settings;
using EventStore.Core.Tests.Authorization;
using EventStore.Core.Util;
using EventRecord = EventStore.Core.Data.EventRecord;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Core.Tests.Services.Transport.Tcp;

[TestFixture]
public class TcpConnectionManagerTests {
	private int _connectionPendingSendBytesThreshold = 10 * 1024;
	private int _connectionQueueSizeThreshold = 50000;

	[Test]
	public void when_handling_trusted_write_on_external_service() {
		var package = new TcpPackage(TcpCommand.WriteEvents, TcpFlags.TrustedWrite, Guid.NewGuid(), null, null,
			new byte[] { });

		var dummyConnection = new DummyTcpConnection();

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(
				InMemoryBus.CreateTest(), new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()),
				new StubPasswordHashAlgorithm(), 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { },
			_connectionPendingSendBytesThreshold, _connectionQueueSizeThreshold);

		tcpConnectionManager.ProcessPackage(package);

		var data = dummyConnection.ReceivedData.Last();
		var receivedPackage = TcpPackage.FromArraySegment(data);

		Assert.AreEqual(receivedPackage.Command, TcpCommand.BadRequest, "Expected Bad Request but got {0}",
			receivedPackage.Command);
	}

	[Test]
	public void when_handling_trusted_write_on_internal_service() {
		ManualResetEvent waiter = new ManualResetEvent(false);
		ClientMessage.WriteEvents publishedWrite = null;
		var evnt = new Event(Guid.NewGuid(), "TestEventType", true, new byte[] { }, new byte[] { });
		var write = new WriteEvents(
			Guid.NewGuid().ToString(),
			ExpectedVersion.Any,
			new[] {
				new NewEvent(evnt.EventId.ToByteArray(), evnt.EventType, evnt.IsJson ? 1 : 0, 0,
					evnt.Data, evnt.Metadata)
			},
			false);

		var package = new TcpPackage(TcpCommand.WriteEvents, Guid.NewGuid(), write.Serialize());
		var dummyConnection = new DummyTcpConnection();
		var publisher = new SynchronousScheduler();

		publisher.Subscribe(new AdHocHandler<ClientMessage.WriteEvents>(x => {
			publishedWrite = x;
			waiter.Set();
		}));

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.Internal, new ClientTcpDispatcher(2000),
			publisher, dummyConnection, publisher,
			new InternalAuthenticationProvider(publisher, new Core.Helpers.IODispatcher(publisher, new NoopEnvelope()),
				new StubPasswordHashAlgorithm(), 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { },
			_connectionPendingSendBytesThreshold, _connectionQueueSizeThreshold);

		tcpConnectionManager.ProcessPackage(package);

		if (!waiter.WaitOne(TimeSpan.FromSeconds(5))) {
			throw new Exception("Timed out waiting for events.");
		}

		Assert.AreEqual(evnt.EventId, publishedWrite.Events.First().EventId,
			"Expected the published write to be the event that was sent through the tcp connection manager to be the event {0} but got {1}",
			evnt.EventId, publishedWrite.Events.First().EventId);
	}

	[Test]
	public void
		when_limit_pending_and_sending_message_smaller_than_threshold_and_pending_bytes_over_threshold_should_close_connection() {
		var mre = new ManualResetEventSlim();

		var messageSize = _connectionPendingSendBytesThreshold / 2;
		var evnt = new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "testStream", 0, DateTime.Now,
			PrepareFlags.None, "eventType", new byte[messageSize], new byte[0]);
		var record = ResolvedEvent.ForUnresolvedEvent(evnt, null);
		var message = new ClientMessage.ReadEventCompleted(Guid.NewGuid(), "testStream", ReadEventResult.Success,
			record, StreamMetadata.Empty, false, "");

		var dummyConnection = new DummyTcpConnection();
		dummyConnection.PendingSendBytes = _connectionPendingSendBytesThreshold + 1000;

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(
				InMemoryBus.CreateTest(), new Core.Helpers.IODispatcher(new SynchronousScheduler(),
					new NoopEnvelope()), null, 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { mre.Set(); },
			_connectionPendingSendBytesThreshold, _connectionQueueSizeThreshold);

		tcpConnectionManager.SendMessage(message);

		if (!mre.Wait(2000)) {
			Assert.Fail("Timed out waiting for connection to close");
		}
	}

	[Test]
	public void
		when_limit_pending_and_sending_message_larger_than_pending_bytes_threshold_but_no_bytes_pending_should_not_close_connection() {
		var messageSize = _connectionPendingSendBytesThreshold + 1000;
		var evnt = new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "testStream", 0, DateTime.Now,
			PrepareFlags.None, "eventType", new byte[messageSize], new byte[0]);
		var record = ResolvedEvent.ForUnresolvedEvent(evnt, null);
		var message = new ClientMessage.ReadEventCompleted(Guid.NewGuid(), "testStream", ReadEventResult.Success,
			record, StreamMetadata.Empty, false, "");

		var dummyConnection = new DummyTcpConnection();
		dummyConnection.PendingSendBytes = 0;

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(InMemoryBus.CreateTest(),
				new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()), null, 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { },
			_connectionPendingSendBytesThreshold, _connectionQueueSizeThreshold);

		tcpConnectionManager.SendMessage(message);

		var data = dummyConnection.ReceivedData.Last();
		var receivedPackage = TcpPackage.FromArraySegment(data);

		Assert.AreEqual(receivedPackage.Command, TcpCommand.ReadEventCompleted,
			"Expected ReadEventCompleted but got {0}", receivedPackage.Command);
	}

	[Test]
	public void
		when_not_limit_pending_and_sending_message_smaller_than_threshold_and_pending_bytes_over_threshold_should_not_close_connection() {
		var mre = new ManualResetEventSlim();

		var messageSize = _connectionPendingSendBytesThreshold / 2;
		var evnt = new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "testStream", 0, DateTime.Now,
			PrepareFlags.None, "eventType", new byte[messageSize], new byte[0]);
		var record = ResolvedEvent.ForUnresolvedEvent(evnt, null);
		var message = new ClientMessage.ReadEventCompleted(Guid.NewGuid(), "testStream", ReadEventResult.Success,
			record, StreamMetadata.Empty, false, "");

		var dummyConnection = new DummyTcpConnection();
		dummyConnection.PendingSendBytes = _connectionPendingSendBytesThreshold + 1000;

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(InMemoryBus.CreateTest(),
				new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()), null, 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { mre.Set(); },
			ESConsts.UnrestrictedPendingSendBytes, ESConsts.MaxConnectionQueueSize);

		tcpConnectionManager.SendMessage(message);

		var data = dummyConnection.ReceivedData.Last();
		var receivedPackage = TcpPackage.FromArraySegment(data);

		Assert.AreEqual(receivedPackage.Command, TcpCommand.ReadEventCompleted,
			"Expected ReadEventCompleted but got {0}", receivedPackage.Command);
	}

	[Test]
	public void
		when_send_queue_size_is_smaller_than_threshold_should_not_close_connection() {
		var mre = new ManualResetEventSlim();

		var messageSize = _connectionPendingSendBytesThreshold;
		var evnt = new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "testStream", 0, DateTime.Now,
			PrepareFlags.None, "eventType", new byte[messageSize], new byte[0]);
		var record = ResolvedEvent.ForUnresolvedEvent(evnt, null);
		var message = new ClientMessage.ReadEventCompleted(Guid.NewGuid(), "testStream", ReadEventResult.Success,
			record, StreamMetadata.Empty, false, "");

		var dummyConnection = new DummyTcpConnection();
		dummyConnection.SendQueueSize = ESConsts.MaxConnectionQueueSize - 1;

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(InMemoryBus.CreateTest(),
				new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()), null, 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { mre.Set(); },
			ESConsts.UnrestrictedPendingSendBytes, ESConsts.MaxConnectionQueueSize);

		tcpConnectionManager.SendMessage(message);

		var data = dummyConnection.ReceivedData.Last();
		var receivedPackage = TcpPackage.FromArraySegment(data);

		Assert.AreEqual(receivedPackage.Command, TcpCommand.ReadEventCompleted,
			"Expected ReadEventCompleted but got {0}", receivedPackage.Command);
	}

	[Test]
	public void
		when_send_queue_size_is_larger_than_threshold_should_close_connection() {
		var mre = new ManualResetEventSlim();

		var messageSize = _connectionPendingSendBytesThreshold;
		var evnt = new EventRecord(0, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, "testStream", 0, DateTime.Now,
			PrepareFlags.None, "eventType", new byte[messageSize], new byte[0]);
		var record = ResolvedEvent.ForUnresolvedEvent(evnt, null);
		var message = new ClientMessage.ReadEventCompleted(Guid.NewGuid(), "testStream", ReadEventResult.Success,
			record, StreamMetadata.Empty, false, "");

		var dummyConnection = new DummyTcpConnection();
		dummyConnection.SendQueueSize = ESConsts.MaxConnectionQueueSize + 1;

		var tcpConnectionManager = new TcpConnectionManager(
			Guid.NewGuid().ToString(), TcpServiceType.External, new ClientTcpDispatcher(2000),
			new SynchronousScheduler(), dummyConnection, new SynchronousScheduler(),
			new InternalAuthenticationProvider(InMemoryBus.CreateTest(),
				new Core.Helpers.IODispatcher(new SynchronousScheduler(), new NoopEnvelope()), null, 1, false, DefaultData.DefaultUserOptions),
			new AuthorizationGateway(new TestAuthorizationProvider()),
			TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), (man, err) => { mre.Set(); },
			ESConsts.UnrestrictedPendingSendBytes, ESConsts.MaxConnectionQueueSize);

		tcpConnectionManager.SendMessage(message);

		if (!mre.Wait(2000)) {
			Assert.Fail("Timed out waiting for connection to close");
		}
	}
}

internal class DummyTcpConnection : ITcpConnection {
	public Guid ConnectionId {
		get { return _connectionId; }
		set { _connectionId = value; }
	}
	private Guid _connectionId = Guid.NewGuid();
	public string ClientConnectionName {
		get { return _clientConnectionName; }
	}

	public long TotalBytesSent { get; }
	public long TotalBytesReceived { get; }

	public bool IsClosed {
		get { return false; }
	}

	public IPEndPoint LocalEndPoint {
		get { return new IPEndPoint(IPAddress.Loopback, 2); }
	}

	public IPEndPoint RemoteEndPoint {
		get { return new IPEndPoint(IPAddress.Loopback, 1); }
	}

	private int _sendQueueSize;
	public int SendQueueSize {
		get { return _sendQueueSize; }
		set { _sendQueueSize = value; }
	}

	private int _pendingSendBytes;

	public int PendingSendBytes {
		get { return _pendingSendBytes; }
		set { _pendingSendBytes = value; }
	}

	public event Action<ITcpConnection, SocketError> ConnectionClosed;
	private string _clientConnectionName;

	public void Close(string reason) {
		var handler = ConnectionClosed;
		if (handler != null)
			handler(this, SocketError.Shutdown);
	}

	public IEnumerable<ArraySegment<byte>> ReceivedData;

	public void EnqueueSend(IEnumerable<ArraySegment<byte>> data) {
		ReceivedData = data;
	}

	public void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback) {
		throw new NotImplementedException();
	}

	public void SetClientConnectionName(string clientConnectionName) {
		_clientConnectionName = clientConnectionName;
	}
}
