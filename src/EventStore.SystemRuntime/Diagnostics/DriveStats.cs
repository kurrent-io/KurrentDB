// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using Serilog;

namespace System.Diagnostics;

public class DriveStats {
	public static readonly ILogger Log = Serilog.Log.ForContext<DriveStats>();

	public static DriveData GetDriveInfo(string path) {
		try {
			var info = new DriveInfo(Path.GetFullPath(path));
			var target = info.Name;
			var diskName = "";

			// info.VolumeLabel looks like what we want but on linux it is just the name again.
			// so we find the best match in the list of drives.
			foreach (var candidate in DriveInfo.GetDrives()) {
				if (target.StartsWith(candidate.Name, StringComparison.InvariantCultureIgnoreCase) &&
					candidate.Name.StartsWith(diskName, StringComparison.InvariantCultureIgnoreCase)) {
					diskName = candidate.Name;
				}
			}

			return new DriveData(diskName, info.TotalSize, info.AvailableFreeSpace);

		} catch (Exception ex) {
			Log.Warning(ex, "Failed to retrieve drive stats for {Path}", path);
			return new DriveData("Unknown", 0, 0);
		}
	}
}

/// <summary>
/// Represents a data structure for drive information.
/// </summary>
/// <param name="DiskName">The name of the disk.</param>
/// <param name="TotalBytes">The total size of the disk in bytes.</param>
/// <param name="AvailableBytes">The available free space on the disk in bytes.</param>
public readonly record struct DriveData(string DiskName, long TotalBytes, long AvailableBytes) 
{
    /// <summary>
    /// The used space on the disk in bytes.
    /// </summary>
    public long UsedBytes { get; } = TotalBytes - AvailableBytes;

    /// <summary>
    /// The usage of the disk as a percentage of the total size.
    /// </summary>
    public int Usage { get; } = (int)(TotalBytes != 0 ? (TotalBytes - AvailableBytes) * 100 / TotalBytes : 0);
}
