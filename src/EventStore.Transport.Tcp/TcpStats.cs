// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Transport.Tcp;

public class TcpStats {
	///<summary>
	///Number of TCP connections to Event Store
	///</summary>
	public readonly int Connections;
	///<summary>
	///Total bytes sent from TCP connections
	///</summary>
	public readonly long SentBytesTotal;
	///<summary>
	///Total bytes received by TCP connections
	///</summary>
	public readonly long ReceivedBytesTotal;
	///<summary>
	///Total bytes sent to TCP connections since last run
	///</summary>
	public readonly long SentBytesSinceLastRun;
	///<summary>
	///Total bytes received by TCP connections since last run
	///</summary>
	public readonly long ReceivedBytesSinceLastRun;
	///<summary>
	///Sending speed in bytes per second
	///</summary>
	public readonly double SendingSpeed;
	///<summary>
	///Receiving speed in bytes per second.
	///</summary>
	public readonly double ReceivingSpeed;
	///<summary>
	///Number of bytes waiting to be sent to connections
	///</summary>
	public readonly long PendingSend;
	///<summary>
	///Number of bytes sent to connections but not yet acknowledged by the receiving party
	///</summary>
	public readonly long InSend;
	///<summary>
	///Number of bytes waiting to be received by connections
	///</summary>
	public readonly long PendingReceived;
	///<summary>
	///Time elapsed since last stats read
	///</summary>
	public readonly TimeSpan MeasureTime;

	public TcpStats(int connections,
		long sentBytesTotal,
		long receivedBytesTotal,
		long sentBytesSinceLastRunSinceLastRun,
		long receivedBytesSinceLastRun,
		long pendingSend,
		long inSend,
		long pendingReceived,
		TimeSpan measureTime) {
		Connections = connections;
		SentBytesTotal = sentBytesTotal;
		ReceivedBytesTotal = receivedBytesTotal;
		SentBytesSinceLastRun = sentBytesSinceLastRunSinceLastRun;
		ReceivedBytesSinceLastRun = receivedBytesSinceLastRun;
		PendingSend = pendingSend;
		InSend = inSend;
		PendingReceived = pendingReceived;
		MeasureTime = measureTime;
		SendingSpeed = (MeasureTime.TotalSeconds < 0.00001) ? 0 : SentBytesSinceLastRun / MeasureTime.TotalSeconds;
		ReceivingSpeed = (MeasureTime.TotalSeconds < 0.00001)
			? 0
			: ReceivedBytesSinceLastRun / MeasureTime.TotalSeconds;
	}
}
