// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Transport.Tcp;

public interface IMonitoredTcpConnection {
	bool IsReadyForSend { get; }
	bool IsReadyForReceive { get; }
	bool IsInitialized { get; }
	bool IsFaulted { get; }
	bool IsClosed { get; }

	bool InSend { get; }
	bool InReceive { get; }

	DateTime? LastSendStarted { get; }
	DateTime? LastReceiveStarted { get; }

	int PendingSendBytes { get; }
	int InSendBytes { get; }
	int PendingReceivedBytes { get; }

	long TotalBytesSent { get; }
	long TotalBytesReceived { get; }
}
