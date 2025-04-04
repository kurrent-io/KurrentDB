// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace EventStore.Transport.Tcp;

public interface ITcpConnection {
	event Action<ITcpConnection, SocketError> ConnectionClosed;

	Guid ConnectionId { get; }
	string ClientConnectionName { get; }
	IPEndPoint RemoteEndPoint { get; }
	IPEndPoint LocalEndPoint { get; }
	int SendQueueSize { get; }
	int PendingSendBytes { get; }
	long TotalBytesSent { get; }
	long TotalBytesReceived { get; }
	bool IsClosed { get; }

	void ReceiveAsync(Action<ITcpConnection, IEnumerable<ArraySegment<byte>>> callback);
	void EnqueueSend(IEnumerable<ArraySegment<byte>> data);
	void Close(string reason);
	void SetClientConnectionName(string clientConnectionName);
}
