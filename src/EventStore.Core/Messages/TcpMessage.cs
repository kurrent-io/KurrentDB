// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Net.Sockets;
using EventStore.Core.Messaging;
using EventStore.Core.Services.Transport.Tcp;

namespace EventStore.Core.Messages;

public static partial class TcpMessage {
	[DerivedMessage(CoreMessage.Tcp)]
	public partial class TcpSend : Message, IQueueAffineMessage {
		public int QueueId {
			get { return ConnectionManager.GetHashCode(); }
		}

		public readonly TcpConnectionManager ConnectionManager;
		public readonly Message Message;

		public TcpSend(TcpConnectionManager connectionManager, Message message) {
			ConnectionManager = connectionManager;
			Message = message;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class Heartbeat : Message {
		public readonly long ReceiveProgressIndicator;
		public readonly long SendProgressIndicator;

		public Heartbeat(long receiveProgressIndicator, long sendProgressIndicator) {
			ReceiveProgressIndicator = receiveProgressIndicator;
			SendProgressIndicator = sendProgressIndicator;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class HeartbeatTimeout : Message {
		public readonly long ReceiveProgressIndicator;

		public HeartbeatTimeout(long receiveProgressIndicator) {
			ReceiveProgressIndicator = receiveProgressIndicator;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class PongMessage : Message {
		public readonly Guid CorrelationId;
		public readonly byte[] Payload;

		public PongMessage(Guid correlationId, byte[] payload) {
			CorrelationId = correlationId;
			Payload = payload;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class ConnectionEstablished : Message {
		public readonly TcpConnectionManager Connection;

		public ConnectionEstablished(TcpConnectionManager connection) {
			Connection = connection;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class ConnectionClosed : Message {
		public readonly TcpConnectionManager Connection;
		public readonly SocketError SocketError;

		public ConnectionClosed(TcpConnectionManager connection, SocketError socketError) {
			Connection = connection;
			SocketError = socketError;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class NotReady : Message {
		public readonly Guid CorrelationId;
		public readonly string Reason;

		public NotReady(Guid correlationId, string reason) {
			CorrelationId = correlationId;
			Reason = reason;
		}
	}


	[DerivedMessage(CoreMessage.Tcp)]
	public partial class NotAuthenticated : Message {
		public readonly Guid CorrelationId;
		public readonly string Reason;

		public NotAuthenticated(Guid correlationId, string reason) {
			CorrelationId = correlationId;
			Reason = reason;
		}
	}

	[DerivedMessage(CoreMessage.Tcp)]
	public partial class Authenticated : Message {
		public readonly Guid CorrelationId;

		public Authenticated(Guid correlationId) {
			CorrelationId = correlationId;
		}
	}
}
