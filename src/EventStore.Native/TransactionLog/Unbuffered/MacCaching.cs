// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Mono.Unix;
using RuntimeInformation = System.Runtime.RuntimeInformation;

namespace EventStore.Core.TransactionLog.Unbuffered;

internal static class MacCaching {
	// ReSharper disable once InconsistentNaming
	private const uint MAC_F_NOCACHE = 48;

	[DllImport("libc")]
	static extern int fcntl(int fd, uint command, int arg);

	public static void Disable(SafeFileHandle handle) {
		if (!RuntimeInformation.IsOSX)
			return;

		long r;
		do {
			r = fcntl(handle.DangerousGetHandle().ToInt32(), MAC_F_NOCACHE, 1);
		} while (UnixMarshal.ShouldRetrySyscall((int)r));

		if (r == -1) {
			UnixMarshal.ThrowExceptionForLastError();
		}
	}
}
