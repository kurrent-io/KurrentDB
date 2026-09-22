// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.Versioning;
using System.Text;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

partial class ClusterStateMachine {
	[SupportedOSPlatform("linux")]
	private static unsafe void FlushToDisk(ReadOnlySpan<char> path, delegate*unmanaged<byte*, int, int, int> openFileFunction) {
		const int O_RDONLY = 0x0000;

		var byteCount = Encoding.UTF8.GetByteCount(path) + 1;
		int fd;
		using (var pathBuffer = (uint)byteCount <= (uint)SpanOwner<byte>.StackallocThreshold
			       ? stackalloc byte[byteCount]
			       : new SpanOwner<byte>(byteCount)) {
			Encoding.UTF8.GetBytes(path, pathBuffer.Span);
			pathBuffer[^1] = 0;

			fixed (byte* pathPtr = pathBuffer) {
				fd = openFileFunction(pathPtr, O_RDONLY, 0);
			}
		}

		if (fd >= 0) {
			using var handle = new SafeFileHandle(fd, ownsHandle: true);
			RandomAccess.FlushToDisk(handle);
		}
	}
}
