// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.IO;

namespace EventStore.Core.Util;

public static class FileUtils {
	public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs) {
		// Get the subdirectories for the specified directory.
		var dir = new DirectoryInfo(sourceDirName);

		if (!dir.Exists)
			throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " +
												 sourceDirName);

		var subdirs = copySubDirs ? dir.GetDirectories() : null;

		// If the destination directory doesn't exist, create it. 
		if (!Directory.Exists(destDirName))
			Directory.CreateDirectory(destDirName);

		// Get the files in the directory and copy them to the new location.
		foreach (FileInfo file in dir.GetFiles()) {
			file.CopyTo(Path.Combine(destDirName, file.Name), false);
		}

		if (copySubDirs) {
			foreach (DirectoryInfo subdir in subdirs) {
				DirectoryCopy(subdir.FullName, Path.Combine(destDirName, subdir.Name), true);
			}
		}
	}
}
