// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Transport.Tcp;

public static class TcpConfiguration {
	public const int SocketCloseTimeoutSecs = 1;

	public const int AcceptBacklogCount = 128;
	public const int ConcurrentAccepts = 1;
	public const int AcceptPoolSize = ConcurrentAccepts * 2;

	public const int ConnectPoolSize = 32;
	public const int SendReceivePoolSize = 512;

	public const int BufferChunksCount = 512;
	public const int SocketBufferSize = 8 * 1024;
}
