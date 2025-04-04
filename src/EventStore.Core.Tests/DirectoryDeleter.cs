// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading.Tasks;

namespace EventStore.Core.Tests;

public static class DirectoryDeleter {
	public static async Task TryForceDeleteDirectoryAsync(string path, int retries = 10) {
		// retry because ClusterNode.StopAsync completes immediately on receiving BecomeShutdown
		// but BecomeShutdown also causes the StorageReaderService to close the index.
		for (var i = 0; i < retries; i++) {
			if (TryForceDeleteDirectory(path))
				return;
			await Task.Delay(1000);
		}
	}
	private static bool TryForceDeleteDirectory(string path) {
		try {
			ForceDeleteDirectory(path);
			return true;
		} catch {
			return false;
		}
	}

	private static void ForceDeleteDirectory(string path) {
		var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };
		foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories)) {
			info.Attributes = FileAttributes.Normal;
		}

		directory.Delete(true);
	}
}
